using ZX.Platform.LowCode.Application.Runtime;
using ZX.Platform.LowCode.Domain;

namespace ZX.Platform.LowCode.Tests;

/// <summary>
/// 记录引擎下传了什么，并按预设结果回应。用它验证"引擎做了哪些检查、传了哪些值"，
/// 而不去验证 SQL——SQL 由 SelectSqlBuilderTests / WriteSqlBuilderTests 负责。
/// </summary>
internal sealed class FakeRecordGateway : IRecordGateway
{
    public List<QuerySpec> Queries { get; } = [];

    public List<CreatedRecord> Creates { get; } = [];

    public List<UpdatedRecord> Updates { get; } = [];

    public List<DeletedRecord> Deletes { get; } = [];

    /// <summary>查询结果。默认空页。</summary>
    public RecordPage Result { get; set; } = RecordPage.Empty(0, 20);

    /// <summary>Update / Delete 是否"改到了行"。false 表示乐观锁失败或记录已删除。</summary>
    public bool WriteSucceeds { get; set; } = true;

    public Task<RecordPage> QueryAsync(QuerySpec spec, CancellationToken cancellationToken)
    {
        Queries.Add(spec);
        return Task.FromResult(Result);
    }

    public Task CreateAsync(
        EntityDefinition entity,
        Guid id,
        IReadOnlyDictionary<string, object?> values,
        RecordScope scope,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        Creates.Add(new CreatedRecord(entity, id, values, scope, now));
        return Task.CompletedTask;
    }

    public Task<bool> UpdateAsync(
        EntityDefinition entity,
        Guid id,
        int revision,
        IReadOnlyDictionary<string, object?> values,
        RecordScope scope,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        Updates.Add(new UpdatedRecord(entity, id, revision, values, scope, now));
        return Task.FromResult(WriteSucceeds);
    }

    public Task<bool> DeleteAsync(
        EntityDefinition entity,
        Guid id,
        int revision,
        RecordScope scope,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        Deletes.Add(new DeletedRecord(entity, id, revision, scope, now));
        return Task.FromResult(WriteSucceeds);
    }

    public sealed record CreatedRecord(
        EntityDefinition Entity,
        Guid Id,
        IReadOnlyDictionary<string, object?> Values,
        RecordScope Scope,
        DateTimeOffset Now);

    public sealed record UpdatedRecord(
        EntityDefinition Entity,
        Guid Id,
        int Revision,
        IReadOnlyDictionary<string, object?> Values,
        RecordScope Scope,
        DateTimeOffset Now);

    public sealed record DeletedRecord(
        EntityDefinition Entity,
        Guid Id,
        int Revision,
        RecordScope Scope,
        DateTimeOffset Now);
}
