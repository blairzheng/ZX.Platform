using Microsoft.EntityFrameworkCore;
using ZX.Platform.Identity.Application.Persistence;
using ZX.Platform.Identity.Application.Users.Dtos;
using ZX.Platform.Identity.Domain.Entities;

namespace ZX.Platform.Identity.Infrastructure.Persistence.Repositories
{
    public sealed class UserRepository : IUserRepository
    {
        private readonly PlatformIdentityDbContext _dbContext;

        public UserRepository(PlatformIdentityDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public Task<User?> GetByIdAsync(Guid userId, CancellationToken cancellationToken)
        {
            return _dbContext.Users
                .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        }

        public Task<User?> GetByNormalizedAccountAsync(string normalizedAccount, CancellationToken cancellationToken)
        {
            return _dbContext.Users
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(u => u.NormalizedAccount == normalizedAccount, cancellationToken);
        }

        public void Add(User user)
        {
            _dbContext.Users.Add(user);
        }

        public async Task<(List<User>, int count)> GetPageListAsync(UserQueryDto userQuery, CancellationToken cancellationToken)
        {
            var users = _dbContext.Users.AsNoTracking();

            if (!string.IsNullOrWhiteSpace(userQuery.Keyword))
            {
                var keyword = userQuery.Keyword.Trim();
                users = users.Where(e => e.Account.Contains(keyword) || e.DisplayName.Contains(keyword));
            }

            if (userQuery.IsEnabled.HasValue)
            {
                users = users.Where(e => e.IsEnabled == userQuery.IsEnabled.Value);
            }

            if (userQuery.FactoryId.HasValue)
            {
                var factoryId = userQuery.FactoryId.Value;
                users = users.Where(user => _dbContext.UserFactories.Any(
                    relation => relation.UserId == user.Id && relation.FactoryId == factoryId));
            }

            var list = await users.OrderByDescending(e => e.CreatedAtUtc)
                .ThenBy(e => e.Id)
                .Skip(userQuery.GetSkip())
                .Take(userQuery.PageSize)
                .ToListAsync(cancellationToken);

            var count = await users.CountAsync(cancellationToken);
            return (list, count);
        }

        public Task<User?> GetByIdIncludeDeletedAsync(Guid userId, CancellationToken cancellationToken)
        {
            return _dbContext.Users
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        }

        public Task<List<User>> GetUsersAsync(List<Guid> userIds, CancellationToken cancellationToken)
        {
            return _dbContext.Users
                .Where(u => userIds.Contains(u.Id))
                .ToListAsync(cancellationToken);
        }
    }
}
