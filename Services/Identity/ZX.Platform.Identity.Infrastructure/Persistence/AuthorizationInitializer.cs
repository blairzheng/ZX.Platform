using Microsoft.EntityFrameworkCore;
using ZX.Platform.Identity.Application.Authorization;
using ZX.Platform.Identity.Application.Exceptions;
using ZX.Platform.Identity.Domain.Entities;

namespace ZX.Platform.Identity.Infrastructure.Persistence;

public sealed class AuthorizationInitializer(PlatformIdentityDbContext db, IPermissionCache cache)
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
