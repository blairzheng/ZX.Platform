namespace ZX.Platform.Identity.Application.Authorization;

public interface IPermissionService
{
    Task<AuthorizationSnapshot> GetAsync(Guid userId, CancellationToken cancellationToken);
    Task<bool> HasAsync(Guid userId, string permission, CancellationToken cancellationToken);
}
