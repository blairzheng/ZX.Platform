using ZX.Platform.Identity.Application.Personnel.Dtos;

namespace ZX.Platform.Identity.Application.Personnel;

public interface IOaPersonnelService
{
    Task<OaPersonnelSyncResultDto> SyncAsync(Guid actorId, CancellationToken cancellationToken);

    Task<OaPersonnelTreeDto> GetTreeAsync(CancellationToken cancellationToken);
}
