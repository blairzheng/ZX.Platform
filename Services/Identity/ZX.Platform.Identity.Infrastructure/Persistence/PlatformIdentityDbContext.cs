using Oracle.ManagedDataAccess.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Oracle.EntityFrameworkCore.Infrastructure;
using ZX.Platform.Identity.Application.Exceptions;
using ZX.Platform.Identity.Application.Persistence;
using ZX.Platform.Identity.Domain.Entities;

namespace ZX.Platform.Identity.Infrastructure.Persistence
{
    public sealed class PlatformIdentityDbContext : DbContext, IUnitOfWork
    {
        public const string DefaultSchema = "ZX_IDENTITY";
        public const string HistoryTable = "ZX_IDENTITY_MIGRATIONS";

        public string Schema { get; }

        public PlatformIdentityDbContext(
            DbContextOptions<PlatformIdentityDbContext> options,
            IdentityDatabaseSchema schema)
            : base(options)
        {
            ValidateSchema(schema.Name);
            Schema = schema.Name;
        }

        public static void Configure(DbContextOptionsBuilder options, string connectionString, string schema)
        {
            ValidateSchema(schema);
            options.ReplaceService<IModelCacheKeyFactory, IdentityDatabaseModelCacheKeyFactory>();
            options.UseOracle(connectionString, oracle =>
            {
                oracle.UseOracleSQLCompatibility(OracleSQLCompatibility.DatabaseVersion19);
                oracle.MigrationsHistoryTable(HistoryTable, schema);
            });
        }

        public static void ValidateSchema(string schema)
        {
            if (string.IsNullOrWhiteSpace(schema) || schema.Length > 128 ||
                schema[0] is not (>= 'A' and <= 'Z') ||
                !schema.All(character => character is >= 'A' and <= 'Z' or >= '0' and <= '9' or '_'))
                throw new InvalidOperationException(
                    "IdentityDatabase:Schema 必须是以大写字母开头、只包含大写字母、数字和下划线的 Oracle 标识符。");
        }

        /// <summary>
        /// 用户数据集。
        /// </summary>
        public DbSet<User> Users => Set<User>();

        /// <summary>
        /// SBU数据集。
        /// </summary>
        public DbSet<Sbu> Sbus => Set<Sbu>();

        /// <summary>
        /// 用户与SBU的关联数据集。
        /// </summary>
        public DbSet<UserSbu> UserSbus => Set<UserSbu>();

        public DbSet<Role> Roles => Set<Role>();
        public DbSet<Permission> Permissions => Set<Permission>();
        public DbSet<UserRole> UserRoles => Set<UserRole>();
        public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
        public DbSet<Menu> Menus => Set<Menu>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // 自动加载当前程序集中的所有 IEntityTypeConfiguration 配置。
            modelBuilder.ApplyConfigurationsFromAssembly(
                typeof(PlatformIdentityDbContext).Assembly);
            modelBuilder.HasDefaultSchema(Schema);
        }

        public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                return await base.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException exception)
            {
                if (exception.InnerException is OracleException { Number: 1 })
                {
                    throw new UniqueConstraintException("保存数据时发生唯一约束冲突。", exception);
                }
                throw;
            }
        }
    }
}
