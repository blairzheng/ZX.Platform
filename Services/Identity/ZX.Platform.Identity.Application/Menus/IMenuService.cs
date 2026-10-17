using ZX.Platform.Identity.Application.Authorization;
using ZX.Platform.Identity.Application.Menus.Dtos;

namespace ZX.Platform.Identity.Application.Menus;

public interface IMenuService
{
    Task<List<MenuDto>> GetTreeAsync(CancellationToken cancellationToken);
    Task<List<MenuDto>> GetCurrentAsync(AuthorizationSnapshot authorization, CancellationToken cancellationToken);
    Task<List<MenuDto>> GetPermissionTreeAsync(CancellationToken cancellationToken);
    Task<Guid> CreateAsync(CreateMenuDto input, CancellationToken cancellationToken);
    Task UpdateAsync(UpdateMenuDto input, CancellationToken cancellationToken);
    Task SetEnabledAsync(Guid id, bool enabled, CancellationToken cancellationToken);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken);
}
