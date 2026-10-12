using Microsoft.EntityFrameworkCore;
using ZX.Platform.Identity.Application.Factories.Dtos;
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

        public async Task<(List<Factory>, int count)> GetPageListAsync(FactoryQueryDto factoryQuery, CancellationToken cancellationToken)
        {
            var factories = _dbContext.Factories.AsNoTracking();

            if (!string.IsNullOrWhiteSpace(factoryQuery.Keyword))
            {
                var keyword = factoryQuery.Keyword.Trim();
                factories = factories.Where(e => e.Code.Contains(keyword) || e.Name.Contains(keyword));
            }

            // 先统计总数，再分页查询，避免分页条件影响总数。
            var count = await factories.CountAsync(cancellationToken);

            var list = await factories.OrderByDescending(e => e.CreatedAtUtc)
                .ThenBy(e => e.Id)
                .Skip(factoryQuery.GetSkip())
                .Take(factoryQuery.PageSize)
                .ToListAsync(cancellationToken);

            return (list, count);
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
