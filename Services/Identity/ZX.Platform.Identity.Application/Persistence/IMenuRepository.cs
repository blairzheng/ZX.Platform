using ZX.Platform.Identity.Domain.Entities;

namespace ZX.Platform.Identity.Application.Persistence;

public interface IMenuRepository
{
    Task<List<Menu>> GetAllAsync(CancellationToken cancellationToken);
    Task<Menu?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken);
    void Add(Menu menu);
}
