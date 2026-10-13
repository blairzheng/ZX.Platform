using Microsoft.EntityFrameworkCore;
using ZX.Platform.Identity.Application.Persistence;
using ZX.Platform.Identity.Domain.Entities;

namespace ZX.Platform.Identity.Infrastructure.Persistence.Repositories
{
    public sealed class FactoryRepository : IFactoryRepository
    {
        private readonly PlatformIdentityDbContext _dbContext;

        public FactoryRepository(PlatformIdentityDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public Task<Factory?> GetByIdAsync(Guid factoryId, CancellationToken cancellationToken)
        {
            return _dbContext.Factories
                .FirstOrDefaultAsync(f => f.Id == factoryId, cancellationToken);
        }

        public Task<Factory?> GetByCodeAsync(string code, CancellationToken cancellationToken)
        {
            return _dbContext.Factories
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(f => f.Code == code, cancellationToken);
        }

        public Task<Factory?> GetByIdIncludeDeletedAsync(Guid factoryId, CancellationToken cancellationToken)
        {
            return _dbContext.Factories
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(f => f.Id == factoryId, cancellationToken);
        }

        public void Add(Factory factory)
        {
            _dbContext.Factories.Add(factory);
        }

        public Task<List<Factory>> GetAllAsync(CancellationToken cancellationToken)
        {
            return _dbContext.Factories.AsNoTracking()
                .OrderByDescending(e => e.CreatedAtUtc)
                .ThenBy(e => e.Id)
                .ToListAsync(cancellationToken);
        }

        /// <summary>
        /// 给前台有效厂区下拉列表
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public Task<List<Factory>> GetAllEnabledFactoriesAsync(CancellationToken cancellationToken)
        {
            return _dbContext.Factories.AsNoTracking().Where(f => f.IsEnabled).ToListAsync(cancellationToken);
        }

        public Task<List<Factory>> GetEnabledFactoriesByIdsAsync(List<Guid> factoryIds, CancellationToken cancellationToken)
        {
            return _dbContext.Factories.AsNoTracking()
            .Where(f => factoryIds.Contains(f.Id) && f.IsEnabled)
            .ToListAsync(cancellationToken);
        }
    }
}
