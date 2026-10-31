using ZX.Platform.LowCode.Domain;

namespace ZX.Platform.LowCode.Application.Runtime;

/// <summary>
/// 业务表的读写出口。Application 只说"查什么 / 写什么"，Dapper + 手写 SQL 由 Infrastructure 负责。
///
/// 写方法返回受影响行数而不是抛错：乐观锁失败、记录已删除、归属不符，在 SQL 层面都是
/// "更新了 0 行"，只有调用方知道该翻译成哪个错误码。
/// </summary>
public interface IRecordGateway
{
    Task<RecordPage> QueryAsync(QuerySpec spec, CancellationToken cancellationToken);

    Task CreateAsync(
        EntityDefinition entity,
        Guid id,
        IReadOnlyDictionary<string, object?> values,
        RecordScope scope,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<bool> UpdateAsync(
        EntityDefinition entity,
        Guid id,
        int revision,
        IReadOnlyDictionary<string, object?> values,
        RecordScope scope,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<bool> DeleteAsync(
        EntityDefinition entity,
        Guid id,
        int revision,
        RecordScope scope,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}
