using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Migrations;
using Oracle.EntityFrameworkCore.Migrations.Internal;

namespace ZX.Platform.Identity.Infrastructure.Persistence;

// 继承当前固定版本的 Oracle 实现，保留其建表包装、迁移锁与存在性检查。
// Oracle 的历史模型单独构建，不经过 PlatformIdentityDbContext.OnModelCreating。
#pragma warning disable EF1001
public sealed class PlatformIdentityHistoryRepository : OracleHistoryRepository
{
    public PlatformIdentityHistoryRepository(HistoryRepositoryDependencies dependencies)
        : base(dependencies)
    {
    }

    protected override void ConfigureTable(EntityTypeBuilder<HistoryRow> history)
    {
        base.ConfigureTable(history);
        history.Property(row => row.MigrationId).HasColumnName("MIGRATION_ID");
        history.Property(row => row.ProductVersion).HasColumnName("PRODUCT_VERSION");

        // 只记录数据库入库时间；迁移历史的 INSERT 仍由提供程序生成，省略此列使用默认值。
        var createTime = history.Property<DateTime>("DatabaseCreateTime")
            .HasColumnName("CREATE_TIME")
            .HasColumnType("DATE")
            .HasDefaultValueSql("SYSDATE")
            .ValueGeneratedOnAdd()
            .IsRequired();
        createTime.Metadata.SetBeforeSaveBehavior(PropertySaveBehavior.Ignore);
        createTime.Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
    }
}
#pragma warning restore EF1001
