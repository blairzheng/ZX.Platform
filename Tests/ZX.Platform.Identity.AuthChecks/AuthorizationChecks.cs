using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ZX.Platform.Identity.Application.Authorization;
using ZX.Platform.Identity.Application.Errors;
using ZX.Platform.Identity.Application.Roles;
using ZX.Platform.Identity.Domain.Entities;
using ZX.Platform.Identity.Domain.Exceptions;
using ZX.Platform.Identity.Infrastructure.Persistence;
using ZX.Platform.Identity.Infrastructure.Security;

namespace ZX.Platform.Identity.AuthChecks;

internal static class AuthorizationChecks
{
    public static async Task RunAsync()
    {
        // 只比较模型并生成 SQL，不打开连接、不执行数据库迁移。
        var options = new DbContextOptionsBuilder<PlatformIdentityDbContext>()
            .UseOracle("User Id=unused;Password=unused;Data Source=localhost/FREEPDB1").Options;
        await using (var db = new PlatformIdentityDbContext(options))
        {
            Check.That(!db.Database.HasPendingModelChanges(), "Authorization mappings must match the committed model snapshot.");
            var sql = db.GetService<IMigrator>().GenerateScript();
            foreach (var table in new[] { "ZX_IDENTITY_ROLE", "ZX_IDENTITY_PERMISSION", "ZX_IDENTITY_USER_ROLE", "ZX_IDENTITY_ROLE_PERMISSION" })
                Check.That(sql.Contains("CREATE TABLE") && sql.Contains($"\"{table}\" ("), $"Migration must create {table}.");
        }

        using var fixture = new Fixture();
        using var otherConnection = TestRedis.Connect();
        var otherCache = new RedisPermissionCache(otherConnection, fixture.RedisOptions);
        PermissionService Reader() => new(fixture.Roles, otherCache);
        var service = new RoleService(fixture.Roles, fixture.Users, fixture.Unit,
            new PermissionService(fixture.Roles, fixture.PermissionCache), fixture.PermissionCache);
        var target = User.Create("VIEWER", "Viewer", fixture.Passwords.Hash("Example123!"));
        fixture.Users.Add(target);
        var readRole = await service.CreateAsync(new() { Code = "reader", Name = "Reader" }, default);
        var editRole = await service.CreateAsync(new() { Code = "editor", Name = "Editor" }, default);
        await service.UpdatePermissionsAsync(new() { RoleId = readRole, PermissionCodes = [IdentityPermissions.UserRead] }, default);
        await service.UpdatePermissionsAsync(new() { RoleId = editRole, PermissionCodes = [IdentityPermissions.UserRename] }, default);
        await service.UpdateUserRolesAsync(fixture.User.Id, new() { UserId = target.Id, RoleIds = [readRole, editRole] }, default);
        var snapshot = await Reader().GetAsync(target.Id, default);
        Check.That(snapshot.Permissions.Contains(IdentityPermissions.UserRead) && snapshot.Permissions.Contains(IdentityPermissions.UserRename),
            "Effective permissions must be the union of enabled roles across Redis connections.");

        var stale = await otherCache.ReadAsync(target.Id, default);
        await service.UpdatePermissionsAsync(new() { RoleId = readRole, PermissionCodes = [] }, default);
        await otherCache.StoreAsync(target.Id, stale.Version, snapshot, default);
        Check.That(!await Reader().HasAsync(target.Id, IdentityPermissions.UserRead, default),
            "An old database query filling the cache after revocation must not restore old permissions.");
        await service.SetEnabledAsync(editRole, false, default);
        Check.That(!await Reader().HasAsync(target.Id, IdentityPermissions.UserRename, default), "Disabled roles must stop authorizing.");
        await service.SetEnabledAsync(editRole, true, default);
        Check.That(await Reader().HasAsync(target.Id, IdentityPermissions.UserRename, default), "Re-enabled roles must reload permissions.");
        await service.DeleteAsync(editRole, default);
        Check.That(!await Reader().HasAsync(target.Id, IdentityPermissions.UserRename, default), "Deleted roles must stop authorizing.");

        await Check.ErrorAsync(() => service.UpdatePermissionsAsync(new() { RoleId = readRole, PermissionCodes = ["identity.removed.action"] }, default),
            ErrorCodes.ValidationFailed);
        var administrator = fixture.Roles.Items.Single(role => role.IsBuiltIn);
        await Check.ErrorAsync(() => service.UpdateUserRolesAsync(target.Id,
            new() { UserId = target.Id, RoleIds = [administrator.Id] }, default), ErrorCodes.Forbidden);
        await Check.ErrorAsync(() => service.UpdateUserRolesAsync(fixture.User.Id,
            new() { UserId = fixture.User.Id, RoleIds = [] }, default), ErrorCodes.InvalidState);
        await Check.ErrorAsync(() => service.EnsureCanDeactivateUsersAsync(fixture.User.Id, [fixture.User.Id], default), ErrorCodes.InvalidState);
        await Check.ErrorAsync(() => service.EnsureCanDeactivateUsersAsync(target.Id, [fixture.User.Id], default), ErrorCodes.Forbidden);
        try
        {
            await service.DeleteAsync(administrator.Id, default);
            throw new InvalidOperationException("Built-in administrator role must be protected.");
        }
        catch (DomainException) { }
        Console.WriteLine("Authorization model, role grants, Redis invalidation and administrator protection passed.");
    }
}
