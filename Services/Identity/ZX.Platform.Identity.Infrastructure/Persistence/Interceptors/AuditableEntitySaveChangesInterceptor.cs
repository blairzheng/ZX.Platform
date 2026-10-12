using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using ZX.Platform.Identity.Domain.Auditing;

namespace ZX.Platform.Identity.Infrastructure.Persistence.Interceptors
{
    public class AuditableEntitySaveChangesInterceptor : SaveChangesInterceptor
    {
        private readonly TimeProvider _timeProvider;

        public AuditableEntitySaveChangesInterceptor(TimeProvider timeProvider)
        {
            _timeProvider = timeProvider;
        }

        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
        {
            ApplyAuditing(eventData.Context);

            return base.SavingChanges(eventData, result);
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            ApplyAuditing(eventData.Context);
            return base.SavingChangesAsync(
                eventData,
                result,
                cancellationToken);
        }

        private void ApplyAuditing(DbContext? dbContext)
        {
            if (dbContext is null)
                return;

            var now = _timeProvider.GetUtcNow();

            foreach (var entry in dbContext.ChangeTracker.Entries<AuditableEntity>())
            {
                if (entry.State == EntityState.Added)
                {
                    entry.Property(x => x.CreatedAtUtc)
                        .CurrentValue = now;
                }

                if (entry.State == EntityState.Modified)
                {
                    // 创建信息不能在修改数据时被覆盖。
                    entry.Property(x => x.CreatedAtUtc)
                        .IsModified = false;

                    entry.Property(x => x.CreatedByUserId)
                        .IsModified = false;

                    entry.Property(x => x.UpdatedAtUtc)
                        .CurrentValue = now;
                }
            }
        }
    }
}
