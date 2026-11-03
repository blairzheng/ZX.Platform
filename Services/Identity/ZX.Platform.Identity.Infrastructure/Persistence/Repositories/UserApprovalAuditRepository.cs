using Microsoft.EntityFrameworkCore;
using ZX.Platform.Identity.Application.Persistence;
using ZX.Platform.Identity.Domain.Entities;

namespace ZX.Platform.Identity.Infrastructure.Persistence.Repositories
{
    public class UserApprovalAuditRepository : IUserApprovalAuditRepository
    {
        private readonly PlatformIdentityDbContext _dbContext;

        public UserApprovalAuditRepository(PlatformIdentityDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public void Add(UserApprovalAudit audit)
        {
            _dbContext.UserApprovalAudits.Add(audit);
        }

        public Task<List<UserApprovalAudit>> GetByTargetUserIdAsync(
            Guid targetUserId, CancellationToken cancellationToken)
        {
            return _dbContext.UserApprovalAudits.AsNoTracking()
                .Where(audit => audit.TargetUserId == targetUserId)
                .OrderByDescending(audit => audit.CreatedAtUtc)
                .ToListAsync(cancellationToken);
        }
    }
}
