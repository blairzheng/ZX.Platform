using ZX.Platform.Identity.Application.Authorization;
using ZX.Platform.Identity.Application.Errors;
using ZX.Platform.Identity.Application.Exceptions;
using ZX.Platform.Identity.Application.Persistence;
using ZX.Platform.Identity.Application.Roles.Dtos;
using ZX.Platform.Identity.Domain.Entities;

namespace ZX.Platform.Identity.Application.Roles;

public sealed class RoleService(
    IRoleRepository roles, IUserRepository users, IUnitOfWork unitOfWork,
    IPermissionService permissions, IPermissionCache cache) : IRoleService
{
    public async Task<List<RoleDto>> GetAllAsync(CancellationToken cancellationToken)
    {
        var list = await roles.GetAllAsync(cancellationToken);
        var relations = await roles.GetPermissionsAsync(list.Select(role => role.Id), cancellationToken);
        var byRole = relations.ToLookup(item => item.RoleId, item => item.PermissionCode);
        return list.Select(role => new RoleDto
        {
            Id = role.Id, Code = role.Code, Name = role.Name, Description = role.Description,
            IsEnabled = role.IsEnabled, IsBuiltIn = role.IsBuiltIn, CreatedAtUtc = role.CreatedAtUtc,
            PermissionCodes = role.IsBuiltIn ? IdentityPermissions.All.ToArray() :
                byRole[role.Id].Where(code => IdentityPermissions.All.Contains(code, StringComparer.Ordinal)).Order(StringComparer.Ordinal).ToArray()
        }).ToList();
    }

    public async Task<Guid> CreateAsync(CreateRoleDto input, CancellationToken cancellationToken)
    {
        var code = input.Code.Trim().ToUpperInvariant();
        if (code == Role.AdministratorCode || await roles.GetByCodeAsync(code, cancellationToken) is not null)
            throw new BusinessException(ErrorCodes.Conflict, "Access.RoleCodeExists");
        var role = Role.Create(code, input.Name, input.Description);
        roles.Add(role);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return role.Id;
    }

    public async Task UpdateAsync(UpdateRoleDto input, CancellationToken cancellationToken)
    {
        var role = await FindAsync(input.Id, cancellationToken);
        role.ChangeDetails(input.Name, input.Description);
        await SaveAsync(cancellationToken);
    }

    public async Task SetEnabledAsync(Guid id, bool enabled, CancellationToken cancellationToken)
    {
        var role = await FindAsync(id, cancellationToken);
        role.SetEnabled(enabled);
        await SaveAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var role = await FindAsync(id, cancellationToken);
        role.EnsureEditable();
        role.MarkDeleted();
        await SaveAsync(cancellationToken);
    }

    public async Task UpdatePermissionsAsync(UpdateRolePermissionsDto input, CancellationToken cancellationToken)
    {
        var role = await FindAsync(input.RoleId, cancellationToken);
        role.EnsureEditable();
        var codes = input.PermissionCodes.ToHashSet(StringComparer.Ordinal);
        if (codes.Any(code => !IdentityPermissions.All.Contains(code, StringComparer.Ordinal)))
            throw new BusinessException(ErrorCodes.ValidationFailed, "Access.UnknownPermission");
        var existing = await roles.GetPermissionsAsync([role.Id], cancellationToken);
        foreach (var relation in existing.Where(item => !codes.Contains(item.PermissionCode))) roles.RemovePermission(relation);
        var old = existing.Select(item => item.PermissionCode).ToHashSet(StringComparer.Ordinal);
        foreach (var code in codes.Where(code => !old.Contains(code))) roles.AddPermission(RolePermission.Create(role.Id, code));
        await SaveAsync(cancellationToken);
    }

    public async Task<List<Guid>> GetUserRolesAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (await users.GetByIdAsync(userId, cancellationToken) is null)
            throw new BusinessException(ErrorCodes.NotFound, "User.NotFound");
        var existing = await roles.GetUserRolesAsync(userId, cancellationToken);
        var activeRoles = await roles.GetByIdsAsync(existing.Select(item => item.RoleId), cancellationToken);
        return activeRoles.Select(role => role.Id).ToList();
    }

    public async Task UpdateUserRolesAsync(Guid actorId, UpdateUserRolesDto input, CancellationToken cancellationToken)
    {
        if (await users.GetByIdAsync(input.UserId, cancellationToken) is null)
            throw new BusinessException(ErrorCodes.NotFound, "User.NotFound");
        var ids = input.RoleIds.ToHashSet();
        var selected = await roles.GetByIdsAsync(ids, cancellationToken);
        var existing = await roles.GetUserRolesAsync(input.UserId, cancellationToken);
        var oldIds = existing.Select(item => item.RoleId).ToHashSet();
        if (selected.Count != ids.Count || selected.Any(role => !role.IsEnabled && !oldIds.Contains(role.Id)))
            throw new BusinessException(ErrorCodes.ValidationFailed, "Access.RoleUnavailable");
        var wasAdministrator = (await roles.GetAdministratorUserIdsAsync([input.UserId], cancellationToken)).Count != 0;
        var becomesAdministrator = selected.Any(role => role.IsBuiltIn && role.Code == Role.AdministratorCode);
        if (wasAdministrator || becomesAdministrator)
        {
            if (!(await permissions.GetAsync(actorId, cancellationToken)).IsAdministrator)
                throw new BusinessException(ErrorCodes.Forbidden, "Auth.Forbidden");
            if (actorId == input.UserId && wasAdministrator && !becomesAdministrator)
                throw new BusinessException(ErrorCodes.InvalidState, "Access.CannotRemoveOwnAdministrator");
        }
        foreach (var relation in existing.Where(item => !ids.Contains(item.RoleId))) roles.RemoveUserRole(relation);
        foreach (var id in ids.Where(id => !oldIds.Contains(id))) roles.AddUserRole(UserRole.Create(input.UserId, id));
        await SaveAsync(cancellationToken);
    }

    public async Task EnsureCanDeactivateUsersAsync(Guid actorId, IEnumerable<Guid> userIds, CancellationToken cancellationToken)
    {
        var ids = userIds.ToHashSet();
        if (ids.Contains(actorId)) throw new BusinessException(ErrorCodes.InvalidState, "Access.CannotDeactivateSelf");
        if ((await roles.GetAdministratorUserIdsAsync(ids, cancellationToken)).Count != 0 &&
            !(await permissions.GetAsync(actorId, cancellationToken)).IsAdministrator)
            throw new BusinessException(ErrorCodes.Forbidden, "Auth.Forbidden");
    }

    private async Task<Role> FindAsync(Guid id, CancellationToken cancellationToken) =>
        await roles.GetByIdAsync(id, cancellationToken) ?? throw new BusinessException(ErrorCodes.NotFound, "Access.RoleNotFound");

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try { await unitOfWork.SaveChangesAsync(cancellationToken); }
        finally
        {
            // 提交结果不确定也使旧权限失效；不使用已取消的客户端令牌。
            await cache.InvalidateAsync(CancellationToken.None);
        }
    }
}
