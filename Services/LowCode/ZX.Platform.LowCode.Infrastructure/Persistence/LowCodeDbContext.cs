using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ZX.Platform.LowCode.Domain.Metadata;

namespace ZX.Platform.LowCode.Infrastructure.Persistence;

/// <summary>
/// 低代码元数据上下文。只管理 6 张静态表，业务表永远不走 EF。
///
/// 刻意不设置默认 schema：表名不带 schema，落在连接账号自己的 schema 里。
/// 这样同一份迁移既能装进 ZX_IDENTITY（开发期），也能装进 ZX_LOWCODE（生产），
/// 而 EF 迁移不会把 schema 名烤进 SQL。
/// </summary>
public sealed class LowCodeDbContext(DbContextOptions<LowCodeDbContext> options) : DbContext(options)
{
    public DbSet<MetaApplication> Applications => Set<MetaApplication>();
    public DbSet<MetaEntityRecord> Entities => Set<MetaEntityRecord>();
    public DbSet<MetaPageRecord> Pages => Set<MetaPageRecord>();
    public DbSet<MetaDatasetRecord> Datasets => Set<MetaDatasetRecord>();
    public DbSet<MetaVersionRecord> Versions => Set<MetaVersionRecord>();
    public DbSet<SchemaChangeRecord> SchemaChanges => Set<SchemaChangeRecord>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        var app = builder.Entity<MetaApplication>();
        ConfigureCommon(app, "LC_META_APP");
        app.HasIndex("Code").IsUnique().HasDatabaseName("UX_LC_META_APP_CODE");

        var entity = builder.Entity<MetaEntityRecord>();
        ConfigureCommon(entity, "LC_META_ENTITY");
        entity.Property("AppCode").HasColumnName("APP_CODE").HasMaxLength(100).IsRequired();
        entity.Property("DraftJson").HasColumnName("DRAFT_JSON").HasColumnType("CLOB");
        entity.Property("PublishedJson").HasColumnName("PUBLISHED_JSON").HasColumnType("CLOB");
        entity.Property("PublishedVersion").HasColumnName("PUBLISHED_VERSION").HasColumnType("NUMBER(10)");
        entity.HasIndex("AppCode", "Code").IsUnique().HasDatabaseName("UX_LC_META_ENTITY_CODE");

        var page = builder.Entity<MetaPageRecord>();
        ConfigureCommon(page, "LC_META_PAGE");
        page.Property("AppCode").HasColumnName("APP_CODE").HasMaxLength(100).IsRequired();
        page.Property("DraftJson").HasColumnName("DRAFT_JSON").HasColumnType("CLOB");
        page.Property("PublishedJson").HasColumnName("PUBLISHED_JSON").HasColumnType("CLOB");
        page.Property("PublishedVersion").HasColumnName("PUBLISHED_VERSION").HasColumnType("NUMBER(10)");
        page.HasIndex("AppCode", "Code").IsUnique().HasDatabaseName("UX_LC_META_PAGE_CODE");

        var dataset = builder.Entity<MetaDatasetRecord>();
        ConfigureCommon(dataset, "LC_META_DATASET");
        dataset.Property("AppCode").HasColumnName("APP_CODE").HasMaxLength(100).IsRequired();
        dataset.Property("DefinitionJson").HasColumnName("DEFINITION_JSON").HasColumnType("CLOB");
        dataset.HasIndex("AppCode", "Code").IsUnique().HasDatabaseName("UX_LC_META_DATASET_CODE");

        var version = builder.Entity<MetaVersionRecord>();
        version.ToTable("LC_META_VERSION");
        version.HasKey("Id");
        version.Property("Id").HasColumnName("ID").HasColumnType("RAW(16)");
        version.Property("OwnerType").HasColumnName("OWNER_TYPE").HasMaxLength(16).IsRequired();
        version.Property("OwnerCode").HasColumnName("OWNER_CODE").HasMaxLength(200).IsRequired();
        version.Property("Version").HasColumnName("VERSION").HasColumnType("NUMBER(10)");
        version.Property("PayloadJson").HasColumnName("PAYLOAD_JSON").HasColumnType("CLOB").IsRequired();
        version.Property("CreatedAtUtc").HasColumnName("CREATED_AT_UTC").HasColumnType("TIMESTAMP(7) WITH TIME ZONE");
        version.Property("CreatedByUserId").HasColumnName("CREATED_BY_USER_ID").HasColumnType("RAW(16)");
        version.HasIndex("OwnerType", "OwnerCode", "Version").IsUnique().HasDatabaseName("UX_LC_META_VERSION_NO");

        var change = builder.Entity<SchemaChangeRecord>();
        change.ToTable("LC_SCHEMA_CHANGE");
        change.HasKey("Id");
        change.Property("Id").HasColumnName("ID").HasColumnType("RAW(16)");
        change.Property("AppCode").HasColumnName("APP_CODE").HasMaxLength(100).IsRequired();
        change.Property("EntityCode").HasColumnName("ENTITY_CODE").HasMaxLength(100).IsRequired();
        change.Property("Kind").HasColumnName("KIND").HasMaxLength(32).IsRequired();
        change.Property("Status").HasColumnName("STATUS").HasMaxLength(16).IsRequired();
        change.Property("PlanJson").HasColumnName("PLAN_JSON").HasColumnType("CLOB").IsRequired();
        change.Property("ScriptSql").HasColumnName("SCRIPT_SQL").HasColumnType("CLOB").IsRequired();
        change.Property("CreatedAtUtc").HasColumnName("CREATED_AT_UTC").HasColumnType("TIMESTAMP(7) WITH TIME ZONE");
        change.Property("CreatedByUserId").HasColumnName("CREATED_BY_USER_ID").HasColumnType("RAW(16)");
        change.Property("ExecutedAtUtc").HasColumnName("EXECUTED_AT_UTC").HasColumnType("TIMESTAMP(7) WITH TIME ZONE");
        change.Property("VerifiedAtUtc").HasColumnName("VERIFIED_AT_UTC").HasColumnType("TIMESTAMP(7) WITH TIME ZONE");
        change.Property("ErrorMessage").HasColumnName("ERROR_MESSAGE").HasMaxLength(2000);
        change.HasIndex("EntityCode", "Status").HasDatabaseName("IX_LC_SCHEMA_CHANGE_ENTITY");
    }

    /// <summary>四个元数据对象共用的列。用字符串 API 而不是表达式，避免接口成员无法映射。</summary>
    private static void ConfigureCommon<T>(EntityTypeBuilder<T> entity, string table) where T : class
    {
        entity.ToTable(table);
        entity.HasKey("Id");
        entity.Property("Id").HasColumnName("ID").HasColumnType("RAW(16)");
        entity.Property("Code").HasColumnName("CODE").HasMaxLength(100).IsRequired();
        entity.Property("Name").HasColumnName("NAME").HasMaxLength(200).IsRequired();
        entity.Property("NameEn").HasColumnName("NAME_EN").HasMaxLength(200);
        entity.Property("Status").HasColumnName("STATUS").HasMaxLength(16).IsRequired();
        entity.Property("Revision").HasColumnName("REVISION").HasColumnType("NUMBER(10)");
        entity.Property("CreatedAtUtc").HasColumnName("CREATED_AT_UTC").HasColumnType("TIMESTAMP(7) WITH TIME ZONE");
        entity.Property("CreatedByUserId").HasColumnName("CREATED_BY_USER_ID").HasColumnType("RAW(16)");
        entity.Property("UpdatedAtUtc").HasColumnName("UPDATED_AT_UTC").HasColumnType("TIMESTAMP(7) WITH TIME ZONE");
        entity.Property("UpdatedByUserId").HasColumnName("UPDATED_BY_USER_ID").HasColumnType("RAW(16)");
        entity.Property("IsDeleted").HasColumnName("IS_DELETED").HasColumnType("NUMBER(1)");
    }
}
