using Microsoft.EntityFrameworkCore;
using ZX.Platform.Identity.Application.Persistence;
using ZX.Platform.Identity.Domain.Entities;

namespace ZX.Platform.Identity.Infrastructure.Persistence.Repositories;

public sealed class MenuRepository(PlatformIdentityDbContext db) : IMenuRepository
{
    public Task<List<Menu>> GetAllAsync(CancellationToken cancellationToken) => db.Menus.AsNoTracking().ToListAsync(cancellationToken);
    public Task<Menu?> GetByIdAsync(Guid id, CancellationToken cancellationToken) => db.Menus.FirstOrDefaultAsync(menu => menu.Id == id, cancellationToken);
    public Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken) => db.Menus.IgnoreQueryFilters().AnyAsync(menu => menu.Code == code, cancellationToken);
    public void Add(Menu menu) => db.Menus.Add(menu);
}
