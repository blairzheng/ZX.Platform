namespace ZX.Platform.LowCode.Domain.Metadata;

/// <summary>元数据对象的生命周期状态。</summary>
public static class MetadataStatus
{
    public const string Draft = "draft";
    public const string Active = "active";
    public const string Disabled = "disabled";
    public const string Retired = "retired";
}

/// <summary>
/// 元数据对象的公共列（不使用继承：EF 会把基类当成 TPH 层次，四张表会互相打架）。
/// 每个元数据类都自带这组属性，配置在 LowCodeDbContext 里逐个显式声明。
/// </summary>
public interface IMetadataRecord
{
    Guid Id { get; set; }
    string Code { get; set; }
    string Name { get; set; }
    string? NameEn { get; set; }
    string Status { get; set; }
    int Revision { get; set; }
    DateTimeOffset CreatedAtUtc { get; set; }
    Guid? CreatedByUserId { get; set; }
    DateTimeOffset? UpdatedAtUtc { get; set; }
    Guid? UpdatedByUserId { get; set; }
    bool IsDeleted { get; set; }
}

/// <summary>应用：业务模块容器，权限与发布的边界。</summary>
public sealed class MetaApplication : IMetadataRecord
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? NameEn { get; set; }
    public string Status { get; set; } = MetadataStatus.Draft;
    public int Revision { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public DateTimeOffset? UpdatedAtUtc { get; set; }
    public Guid? UpdatedByUserId { get; set; }
    public bool IsDeleted { get; set; }
}

/// <summary>
/// 实体元数据。字段与视图整体存在 JSON 里，不拆表——
/// 元数据永远是整体加载的聚合，拆表只会把写入变成跨表事务。
/// </summary>
public sealed class MetaEntityRecord : IMetadataRecord
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? NameEn { get; set; }
    public string Status { get; set; } = MetadataStatus.Draft;
    public int Revision { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public DateTimeOffset? UpdatedAtUtc { get; set; }
    public Guid? UpdatedByUserId { get; set; }
    public bool IsDeleted { get; set; }

    public string AppCode { get; set; } = string.Empty;
    public string? DraftJson { get; set; }
    public string? PublishedJson { get; set; }
    public int PublishedVersion { get; set; }
}

/// <summary>页面元数据。与实体同构：草稿 + 已发布快照 + 版本号。</summary>
public sealed class MetaPageRecord : IMetadataRecord
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? NameEn { get; set; }
    public string Status { get; set; } = MetadataStatus.Draft;
    public int Revision { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public DateTimeOffset? UpdatedAtUtc { get; set; }
    public Guid? UpdatedByUserId { get; set; }
    public bool IsDeleted { get; set; }

    public string AppCode { get; set; } = string.Empty;
    public string? DraftJson { get; set; }
    public string? PublishedJson { get; set; }
    public int PublishedVersion { get; set; }
}

/// <summary>外部只读数据集：连接别名 + 视图 + 字段白名单，不含凭据。</summary>
public sealed class MetaDatasetRecord : IMetadataRecord
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? NameEn { get; set; }
    public string Status { get; set; } = MetadataStatus.Draft;
    public int Revision { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public DateTimeOffset? UpdatedAtUtc { get; set; }
    public Guid? UpdatedByUserId { get; set; }
    public bool IsDeleted { get; set; }

    public string AppCode { get; set; } = string.Empty;
    public string? DefinitionJson { get; set; }
}

/// <summary>发布版本快照。各对象共用一张表，OwnerType 区分。</summary>
public sealed class MetaVersionRecord
{
    public Guid Id { get; set; }
    public string OwnerType { get; set; } = string.Empty;
    public string OwnerCode { get; set; } = string.Empty;
    public int Version { get; set; }
    public string PayloadJson { get; set; } = "{}";
    public DateTimeOffset CreatedAtUtc { get; set; }
    public Guid? CreatedByUserId { get; set; }
}

/// <summary>结构变更的状态机。元数据版本能否激活，只看依赖的变更是否全部 Applied。</summary>
public static class SchemaChangeStatus
{
    public const string Pending = "pending";
    public const string Executing = "executing";
    public const string Exported = "exported";
    public const string Verified = "verified";
    public const string Applied = "applied";
    public const string Failed = "failed";
}

/// <summary>结构变更的种类。</summary>
public static class SchemaChangeKind
{
    public const string CreateTable = "create_table";
    public const string AddColumn = "add_column";
    public const string AddIndex = "add_index";
    public const string DropTable = "drop_table";
}

/// <summary>
/// 一次结构变更计划。SQL 只是这份记录的渲染结果——
/// 测试环境执行它，生产环境导出同一份，不允许两条代码路径。
/// </summary>
public sealed class SchemaChangeRecord
{
    public Guid Id { get; set; }
    public string AppCode { get; set; } = string.Empty;
    public string EntityCode { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string Status { get; set; } = SchemaChangeStatus.Pending;
    public string PlanJson { get; set; } = "{}";
    public string ScriptSql { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public DateTimeOffset? ExecutedAtUtc { get; set; }
    public DateTimeOffset? VerifiedAtUtc { get; set; }
    public string? ErrorMessage { get; set; }
}
