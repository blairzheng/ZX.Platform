using ZX.Platform.Identity.Application.Persistence;

namespace ZX.Platform.Identity.Application.Authorization;

public sealed class PermissionService(IRoleRepository roles, IPermissionCache cache, IUserRepository users) : IPermissionService
{
    // 只在当前 HTTP 请求内复用；没有跨请求的本机权限缓存。
    private readonly Dictionary<Guid, AuthorizationSnapshot> _snapshots = [];

    public async Task<AuthorizationSnapshot> GetAsync(Guid userId, CancellationToken cancellationToken)
    {
        // 状态闸门必须先于缓存：Pending 的旧快照或提前分配的管理员角色也不得授权。
        var user = await users.GetByIdAsync(userId, cancellationToken);
        if (user?.HasBusinessAccess != true) return new AuthorizationSnapshot(false, [], []);
        if (_snapshots.TryGetValue(userId, out var existing)) return existing;
        var cached = await cache.ReadAsync(userId, cancellationToken);
        var snapshot = cached.Snapshot;
        if (snapshot is null)
        {
            snapshot = await roles.GetAuthorizationAsync(userId, cancellationToken);
            await cache.StoreAsync(userId, cached.Version, snapshot, cancellationToken);
        }
        _snapshots[userId] = snapshot;
        return snapshot;
    }

    public async Task<bool> HasAsync(Guid userId, string permission, CancellationToken cancellationToken)
    {
        if (!IdentityPermissions.All.Contains(permission, StringComparer.Ordinal)) return false;
        var snapshot = await GetAsync(userId, cancellationToken);
        return snapshot.IsAdministrator || snapshot.Permissions.Contains(permission, StringComparer.Ordinal);
    }
}
