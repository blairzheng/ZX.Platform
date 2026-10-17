using Microsoft.EntityFrameworkCore;
using ZX.Platform.Identity.Application.Authorization;
using ZX.Platform.Identity.Application.Persistence;
using ZX.Platform.Identity.Domain.Entities;

namespace ZX.Platform.Identity.Infrastructure.Persistence.Repositories;

public sealed class RoleRepository(PlatformIdentityDbContext db) : IRoleRepository
{
    public void Add(Role role) => db.Roles.Add(role);
    public Task<Role?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        db.Roles.FirstOrDefaultAsync(role => role.Id == id, cancellationToken);
    public Task<Role?> GetByCodeAsync(string code, CancellationToken cancellationToken) =>
        db.Roles.IgnoreQueryFilters().FirstOrDefaultAsync(role => role.Code == code, cancellationToken);
    public Task<List<Role>> GetAllAsync(CancellationToken cancellationToken) =>
        db.Roles.AsNoTracking().OrderByDescending(role => role.IsBuiltIn).ThenBy(role => role.Code).ToListAsync(cancellationToken);
    public Task<List<Role>> GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken) =>
        db.Roles.Where(role => ids.Contains(role.Id)).ToListAsync(cancellationToken);
    public Task<List<RolePermission>> GetPermissionsAsync(IEnumerable<Guid> roleIds, CancellationToken cancellationToken) =>
        db.RolePermissions.Where(relation => roleIds.Contains(relation.RoleId)).ToListAsync(cancellationToken);
    public void AddPermission(RolePermission permission) => db.RolePermissions.Add(permission);
    public void RemovePermission(RolePermission permission) => db.RolePermissions.Remove(permission);
    public Task<List<UserRole>> GetUserRolesAsync(Guid userId, CancellationToken cancellationToken) =>
        db.UserRoles.Where(relation => relation.UserId == userId).ToListAsync(cancellationToken);
    public void AddUserRole(UserRole relation) => db.UserRoles.Add(relation);
    public void RemoveUserRole(UserRole relation) => db.UserRoles.Remove(relation);

    public Task<List<Guid>> GetAdministratorUserIdsAsync(IEnumerable<Guid> userIds, CancellationToken cancellationToken) =>
        db.UserRoles
            .Where(relation => userIds.Contains(relation.UserId))
            .Join(
                db.Roles.Where(role => role.IsBuiltIn && role.Code == Role.AdministratorCode && role.IsEnabled),
                relation => relation.RoleId,
                role => role.Id,
                (relation, role) => relation.UserId)
            .Distinct()
            .ToListAsync(cancellationToken);

    public async Task<AuthorizationSnapshot> GetAuthorizationAsync(Guid userId, CancellationToken cancellationToken)
    {
        var userRoles = await db.UserRoles
            .Where(relation => relation.UserId == userId)
            .Join(
                db.Roles.Where(role => role.IsEnabled),
                relation => relation.RoleId,
                role => role.Id,
                (relation, role) => new { relation.UserId, role.Id, role.Code, role.IsBuiltIn })
            .Join(
                db.Users.Where(user => user.IsEnabled),
                role => role.UserId,
                user => user.Id,
                (role, user) => new { role.Id, role.Code, role.IsBuiltIn })
            .ToListAsync(cancellationToken);
        var administrator = userRoles.Any(role => role.IsBuiltIn && role.Code == Role.AdministratorCode);
        var roleIds = userRoles.Select(role => role.Id).ToArray();
        var codes = administrator ? IdentityPermissions.All.ToArray() : await db.RolePermissions
            .Where(relation => roleIds.Contains(relation.RoleId))
            .Join(
                db.Permissions.Where(permission => permission.IsActive),
                relation => relation.PermissionCode,
                permission => permission.Code,
                (relation, permission) => permission.Code)
            .Distinct()
            .ToArrayAsync(cancellationToken);
        return new AuthorizationSnapshot(administrator, userRoles.Select(role => role.Code).Order(StringComparer.Ordinal).ToArray(),
            codes.Where(code => IdentityPermissions.All.Contains(code, StringComparer.Ordinal)).Order(StringComparer.Ordinal).ToArray());
    }
}
