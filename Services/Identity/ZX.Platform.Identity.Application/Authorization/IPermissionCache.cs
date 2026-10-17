namespace ZX.Platform.Identity.Application.Authorization;

public interface IPermissionCache
{
    Task<CachedAuthorization> ReadAsync(Guid userId, CancellationToken cancellationToken);
    Task StoreAsync(Guid userId, string version, AuthorizationSnapshot snapshot, CancellationToken cancellationToken);
    Task InvalidateAsync(CancellationToken cancellationToken);
}
