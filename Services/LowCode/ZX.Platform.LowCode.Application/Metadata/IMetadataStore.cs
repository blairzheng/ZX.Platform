using ZX.Platform.LowCode.Domain;
using ZX.Platform.LowCode.Domain.Metadata;

namespace ZX.Platform.LowCode.Application.Metadata;

/// <summary>
/// 元数据仓储。接口放在 Application，实现放在 Infrastructure，
/// 这样 MetadataService 可以用内存实现做单测，不需要数据库。
/// </summary>
public interface IMetadataStore
{
    Task<MetaEntityRecord?> FindEntityAsync(string appCode, string code, CancellationToken cancellationToken);

    Task<IReadOnlyList<MetaEntityRecord>> ListEntitiesAsync(string appCode, CancellationToken cancellationToken);

    Task AddEntityAsync(MetaEntityRecord record, CancellationToken cancellationToken);

    Task<IReadOnlyList<MetaVersionRecord>> ListVersionsAsync(
        string ownerType, string ownerCode, CancellationToken cancellationToken);

    Task AddVersionAsync(MetaVersionRecord record, CancellationToken cancellationToken);

    /// <summary>
    /// 该实体尚未执行完成的结构变更。
    ///
    /// 判据是"状态不是 applied"，不是"状态等于 pending"：导出了、执行中、执行失败
    /// 都算没完成。否则变更一旦从 pending 走到 exported，发布闸门就失效了，
    /// 元数据会先激活、表还没建，运行时立刻 500。
    /// </summary>
    Task<IReadOnlyList<SchemaChangeRecord>> ListPendingChangesAsync(
        string entityCode, CancellationToken cancellationToken);

    Task<SchemaChangeRecord?> FindChangeAsync(Guid id, CancellationToken cancellationToken);

    Task AddChangesAsync(IEnumerable<SchemaChangeRecord> changes, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

/// <summary>一条待执行的结构变更，由规划器产出。</summary>
public sealed record PlannedChange(string Kind, IReadOnlyList<string> Statements, string PlanJson)
{
    public string Script => string.Join(";\n", Statements);
}

/// <summary>
/// 结构变更规划器。输入"当前草稿"和"库里这张表的现状"，输出还差哪些 DDL。
///
/// 不接收"上一次发布的定义"：那份定义描述的是**平台以为**的库，不是库本身。
/// </summary>
public interface ISchemaChangePlanner
{
    IReadOnlyList<PlannedChange> Plan(EntityDefinition draft, TableShape shape);
}

/// <summary>发布结果。存在待执行变更时，元数据不会激活。</summary>
public sealed record PublishResult(
    bool Published,
    int Version,
    IReadOnlyList<SchemaChangeRecord> PendingChanges)
{
    public static PublishResult Blocked(IReadOnlyList<SchemaChangeRecord> changes) => new(false, 0, changes);

    public static PublishResult Done(int version) => new(true, version, []);
}
