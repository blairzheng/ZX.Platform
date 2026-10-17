using Microsoft.EntityFrameworkCore;
using ZX.Platform.Identity.Application.Authentication;
using ZX.Platform.Identity.Application.Authorization;
using ZX.Platform.Identity.Application.Exceptions;
using ZX.Platform.Identity.Domain.Entities;

namespace ZX.Platform.Identity.Infrastructure.Persistence;

public sealed class AuthorizationInitializer(PlatformIdentityDbContext db, AuthOptions options, IPermissionCache cache)
{
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        // 多实例首次启动可能同时插入目录；唯一索引裁决后重新读取，不用分布式锁。
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                var definitions = IdentityPermissions.All.ToHashSet(StringComparer.Ordinal);
                var stored = await db.Permissions.ToListAsync(cancellationToken);
                foreach (var permission in stored) permission.SetActive(definitions.Contains(permission.Code));
                foreach (var code in definitions.Except(stored.Select(item => item.Code), StringComparer.Ordinal))
                    db.Permissions.Add(Permission.Create(code));

                var administrator = await db.Roles.IgnoreQueryFilters().SingleOrDefaultAsync(role => role.Code == Role.AdministratorCode, cancellationToken);
                // 配置仅用于首次引导；角色存在后以数据库分配为准，避免重启恢复已撤销的管理员权限。
                if (administrator is null && options.AdministratorAccounts.Length > 0)
                {
                    var accounts = options.AdministratorAccounts.Select(account => account.Trim().ToUpperInvariant()).Distinct().ToArray();
                    var users = await db.Users.Where(user => accounts.Contains(user.NormalizedAccount) && user.IsEnabled).ToListAsync(cancellationToken);
                    if (users.Count != accounts.Length)
                        throw new InvalidOperationException("首次角色初始化：AdministratorAccounts 必须全部对应已有、启用且未删除的用户。");
                    administrator = Role.CreateAdministrator();
                    db.Roles.Add(administrator);
                    foreach (var user in users) db.UserRoles.Add(UserRole.Create(user.Id, administrator.Id));
                }
                var storedMenus = await db.Menus.IgnoreQueryFilters().ToListAsync(cancellationToken);
                DefaultMenus.AddMissing(storedMenus, menu => db.Menus.Add(menu));
                await db.SaveChangesAsync(cancellationToken);
                break;
            }
            catch (UniqueConstraintException) when (attempt == 0)
            {
                db.ChangeTracker.Clear();
            }
        }
        // 包括停用旧权限；不得继续使用前一版本的缓存。
        await cache.InvalidateAsync(CancellationToken.None);
    }
}
