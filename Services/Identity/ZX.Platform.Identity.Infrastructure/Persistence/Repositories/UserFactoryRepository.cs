using Microsoft.EntityFrameworkCore;
using ZX.Platform.Identity.Application.Persistence;
using ZX.Platform.Identity.Domain.Entities;

namespace ZX.Platform.Identity.Infrastructure.Persistence.Repositories
{
    public class UserFactoryRepository : IUserFactoryRepository
    {
        private readonly PlatformIdentityDbContext _dbContext;

        public UserFactoryRepository(PlatformIdentityDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public void Add(UserFactory userFactory)
        {
            _dbContext.UserFactories.Add(userFactory);
        }

        public Task<List<UserFactory>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken)
        {
            return _dbContext.UserFactories.Where(uf => uf.UserId == userId).ToListAsync(cancellationToken);
        }

        public Task<List<UserFactory>> GetByUserIdsAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken)
        {
            if (userIds.Count == 0) return Task.FromResult(new List<UserFactory>());
            return _dbContext.UserFactories.AsNoTracking()
                .Where(uf => userIds.Contains(uf.UserId))
                .ToListAsync(cancellationToken);
        }

        public void Remove(UserFactory userFactory)
        {
            _dbContext.UserFactories.Remove(userFactory);
        }
    }
}
