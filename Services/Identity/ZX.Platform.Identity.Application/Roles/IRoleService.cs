using ZX.Platform.Identity.Application.Roles.Dtos;

namespace ZX.Platform.Identity.Application.Roles;

public interface IRoleService
{
    Task<List<RoleDto>> GetAllAsync(CancellationToken cancellationToken);
    Task<Guid> CreateAsync(CreateRoleDto input, CancellationToken cancellationToken);
    Task UpdateAsync(UpdateRoleDto input, CancellationToken cancellationToken);
    Task SetEnabledAsync(Guid id, bool enabled, CancellationToken cancellationToken);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken);
    Task UpdatePermissionsAsync(UpdateRolePermissionsDto input, CancellationToken cancellationToken);
    Task<List<Guid>> GetUserRolesAsync(Guid userId, CancellationToken cancellationToken);
    Task UpdateUserRolesAsync(Guid actorId, UpdateUserRolesDto input, CancellationToken cancellationToken);
    Task EnsureCanDeactivateUsersAsync(Guid actorId, IEnumerable<Guid> userIds, CancellationToken cancellationToken);
}
