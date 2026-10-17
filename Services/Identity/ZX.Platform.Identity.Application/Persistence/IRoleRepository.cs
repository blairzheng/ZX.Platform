using ZX.Platform.Identity.Application.Authorization;
using ZX.Platform.Identity.Domain.Entities;

namespace ZX.Platform.Identity.Application.Persistence;

public interface IRoleRepository
{
    void Add(Role role);
    Task<Role?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<Role?> GetByCodeAsync(string code, CancellationToken cancellationToken);
    Task<List<Role>> GetAllAsync(CancellationToken cancellationToken);
    Task<List<Role>> GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken);
    Task<List<RolePermission>> GetPermissionsAsync(IEnumerable<Guid> roleIds, CancellationToken cancellationToken);
    void AddPermission(RolePermission permission);
    void RemovePermission(RolePermission permission);
    Task<List<UserRole>> GetUserRolesAsync(Guid userId, CancellationToken cancellationToken);
    void AddUserRole(UserRole relation);
    void RemoveUserRole(UserRole relation);
    Task<List<Guid>> GetAdministratorUserIdsAsync(IEnumerable<Guid> userIds, CancellationToken cancellationToken);
    Task<AuthorizationSnapshot> GetAuthorizationAsync(Guid userId, CancellationToken cancellationToken);
}
