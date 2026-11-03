using Microsoft.EntityFrameworkCore;
using ZX.Platform.Identity.Application.Persistence;
using ZX.Platform.Identity.Domain.Entities;

namespace ZX.Platform.Identity.Infrastructure.Persistence.Repositories
{
    public sealed class SbuRepository : ISbuRepository
    {
        private readonly PlatformIdentityDbContext _dbContext;

        public SbuRepository(PlatformIdentityDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public Task<Sbu?> GetByIdAsync(Guid sbuId, CancellationToken cancellationToken)
        {
            return _dbContext.Sbus
                .FirstOrDefaultAsync(f => f.Id == sbuId, cancellationToken);
        }

        public Task<Sbu?> GetByCodeAsync(string code, CancellationToken cancellationToken)
        {
            return _dbContext.Sbus
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(f => f.Code == code, cancellationToken);
        }

        public Task<Sbu?> GetByIdIncludeDeletedAsync(Guid sbuId, CancellationToken cancellationToken)
        {
            return _dbContext.Sbus
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(f => f.Id == sbuId, cancellationToken);
        }

        public void Add(Sbu sbu)
        {
            _dbContext.Sbus.Add(sbu);
        }

        public Task<List<Sbu>> GetAllAsync(CancellationToken cancellationToken)
        {
            return _dbContext.Sbus.AsNoTracking()
                .OrderByDescending(e => e.CreatedAtUtc)
                .ThenBy(e => e.Id)
                .ToListAsync(cancellationToken);
        }

        /// <summary>
        /// 给前台有效SBU下拉列表
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public Task<List<Sbu>> GetAllEnabledSbusAsync(CancellationToken cancellationToken)
        {
            return _dbContext.Sbus.AsNoTracking().Where(f => f.IsEnabled).ToListAsync(cancellationToken);
        }

        public Task<List<Sbu>> GetEnabledSbusByIdsAsync(List<Guid> sbuIds, CancellationToken cancellationToken)
        {
            return _dbContext.Sbus.AsNoTracking()
            .Where(f => sbuIds.Contains(f.Id) && f.IsEnabled)
            .ToListAsync(cancellationToken);
        }
    }
}
