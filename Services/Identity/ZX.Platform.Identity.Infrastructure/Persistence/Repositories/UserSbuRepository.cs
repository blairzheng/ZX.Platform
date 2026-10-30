using Microsoft.EntityFrameworkCore;
using ZX.Platform.Identity.Application.Persistence;
using ZX.Platform.Identity.Domain.Entities;

namespace ZX.Platform.Identity.Infrastructure.Persistence.Repositories
{
    public class UserSbuRepository : IUserSbuRepository
    {
        private readonly PlatformIdentityDbContext _dbContext;

        public UserSbuRepository(PlatformIdentityDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public void Add(UserSbu userSbu)
        {
            _dbContext.UserSbus.Add(userSbu);
        }

        public Task<List<UserSbu>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken)
        {
            return _dbContext.UserSbus.Where(uf => uf.UserId == userId).ToListAsync(cancellationToken);
        }

        public Task<Sbu?> GetEnabledSbuForUserAsync(Guid userId, Guid sbuId, CancellationToken cancellationToken)
        {
            return _dbContext.Sbus.AsNoTracking()
                .Where(sbu => sbu.Id == sbuId && sbu.IsEnabled && !sbu.IsDeleted &&
                    _dbContext.UserSbus.Any(link => link.UserId == userId && link.SbuId == sbu.Id))
                .FirstOrDefaultAsync(cancellationToken);
        }

        public Task<List<UserSbu>> GetByUserIdsAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken)
        {
            if (userIds.Count == 0) return Task.FromResult(new List<UserSbu>());
            return _dbContext.UserSbus.AsNoTracking()
                .Where(uf => userIds.Contains(uf.UserId))
                .ToListAsync(cancellationToken);
        }

        public void Remove(UserSbu userSbu)
        {
            _dbContext.UserSbus.Remove(userSbu);
        }
    }
}
