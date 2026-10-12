using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using ZX.Platform.Identity.Application.Exceptions;
using ZX.Platform.Identity.Application.Persistence;
using ZX.Platform.Identity.Domain.Entities;

namespace ZX.Platform.Identity.Infrastructure.Persistence
{
    public sealed class PlatformIdentityDbContext : DbContext, IUnitOfWork
    {
        public PlatformIdentityDbContext(
            DbContextOptions<PlatformIdentityDbContext> options)
            : base(options)
        {
        }

        /// <summary>
        /// 用户数据集。
        /// </summary>
        public DbSet<User> Users => Set<User>();

        /// <summary>
        /// 厂区数据集。
        /// </summary>
        public DbSet<Factory> Factories => Set<Factory>();

        /// <summary>
        /// 用户与厂区的关联数据集。
        /// </summary>
        public DbSet<UserFactory> UserFactories => Set<UserFactory>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // 自动加载当前程序集中的所有 IEntityTypeConfiguration 配置。
            modelBuilder.ApplyConfigurationsFromAssembly(
                typeof(PlatformIdentityDbContext).Assembly);
        }

        public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                return await base.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException exception)
            {
                if (exception.InnerException is SqlException sqlException && sqlException.Number is 2601 or 2627)
                {
                    throw new UniqueConstraintException("保存数据时发生唯一约束冲突。", exception);
                }
                throw;
            }
        }
    }
}
