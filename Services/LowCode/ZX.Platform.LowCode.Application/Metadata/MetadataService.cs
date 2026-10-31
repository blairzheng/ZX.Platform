using ZX.Platform.LowCode.Domain;
using ZX.Platform.LowCode.Domain.Metadata;

namespace ZX.Platform.LowCode.Application.Metadata;

/// <summary>
/// 元数据的草稿 / 发布 / 版本。四条规则写死在这里：
///
/// 1. 保存草稿必须带 revision，否则并发编辑会互相覆盖。
/// 2. 发布前必须校验，校验不过一律 600001。
/// 3. **有结构变更未执行时不允许发布**——元数据先激活、表后建，运行时立刻 500。
/// 4. 结构变更的差异以**数据库现状**为基线，不以已发布定义为基线。
/// </summary>
public sealed class MetadataService(
    IMetadataStore store,
    ISchemaInspector inspector,
    ISchemaChangePlanner planner,
    TimeProvider? clock = null)
{
    public const string EntityOwnerType = "entity";

    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    public async Task<MetaEntityRecord> CreateAsync(
        string appCode,
        EntityDefinition draft,
        Guid? userId,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(draft.AppCode, appCode, StringComparison.Ordinal))
            throw new LowCodeException(LowCodeErrorCodes.ValidationFailed, "实体所属应用与请求不一致。");

        var existing = await store.FindEntityAsync(appCode, draft.Code, cancellationToken);
        if (existing is not null)
            throw new LowCodeException(LowCodeErrorCodes.Conflict, $"实体「{draft.Code}」已存在。");

        EnsureValid(draft);

        var record = new MetaEntityRecord
        {
            Id = Guid.NewGuid(),
            AppCode = appCode,
            Code = draft.Code,
            Name = draft.Name,
            NameEn = draft.NameEn,
            Status = MetadataStatus.Draft,
            Revision = 1,
            DraftJson = MetadataJson.Serialize(draft),
            CreatedAtUtc = _clock.GetUtcNow(),
            CreatedByUserId = userId
        };

        await store.AddEntityAsync(record, cancellationToken);
        await store.SaveChangesAsync(cancellationToken);
        return record;
    }

    public async Task<EntityDefinition> GetDraftAsync(
        string appCode, string code, CancellationToken cancellationToken)
    {
        var record = await RequireEntityAsync(appCode, code, cancellationToken);
        if (string.IsNullOrWhiteSpace(record.DraftJson))
            throw new LowCodeException(LowCodeErrorCodes.NotFound, $"实体「{code}」还没有草稿。");

        return MetadataJson.Deserialize<EntityDefinition>(record.DraftJson);
    }

    /// <summary>运行时读这个：只有已发布的定义才对外可见。</summary>
    public async Task<EntityDefinition?> GetPublishedAsync(
        string appCode, string code, CancellationToken cancellationToken)
    {
        var record = await store.FindEntityAsync(appCode, code, cancellationToken);
        if (record is null || string.IsNullOrWhiteSpace(record.PublishedJson)) return null;

        return MetadataJson.Deserialize<EntityDefinition>(record.PublishedJson);
    }

    /// <summary>
    /// 运行时读这个：拿不到就 600002。
    /// 草稿态必须报"不存在"而不是"未发布"——否则等于告诉调用方有个还没上线的实体。
    /// </summary>
    public async Task<EntityDefinition> RequirePublishedAsync(
        string appCode, string code, CancellationToken cancellationToken)
    {
        var entity = await GetPublishedAsync(appCode, code, cancellationToken);
        return entity ?? throw new LowCodeException(
            LowCodeErrorCodes.NotFound, $"实体「{code}」不存在或尚未发布。");
    }

    public async Task<EntityDefinition> SaveDraftAsync(
        string appCode,
        string code,
        EntityDefinition draft,
        int revision,
        Guid? userId,
        CancellationToken cancellationToken)
    {
        var record = await RequireEntityAsync(appCode, code, cancellationToken);

        if (record.Revision != revision)
            throw new LowCodeException(LowCodeErrorCodes.Conflict,
                $"草稿已被修改（当前 revision = {record.Revision}），请刷新后重试。");

        if (!string.Equals(draft.Code, code, StringComparison.Ordinal))
            throw new LowCodeException(LowCodeErrorCodes.ValidationFailed, "草稿的实体编码不能修改。");

        EnsureValid(draft);

        record.DraftJson = MetadataJson.Serialize(draft);
        record.Name = draft.Name;
        record.NameEn = draft.NameEn;
        record.Revision += 1;
        record.UpdatedAtUtc = _clock.GetUtcNow();
        record.UpdatedByUserId = userId;

        await store.SaveChangesAsync(cancellationToken);
        return draft;
    }

    /// <summary>
    /// 发布。存在待执行的结构变更时**不发布**，先把变更交出去执行，再回来发布。
    /// </summary>
    public async Task<PublishResult> PublishAsync(
        string appCode, string code, Guid? userId, CancellationToken cancellationToken)
    {
        var record = await RequireEntityAsync(appCode, code, cancellationToken);

        if (string.IsNullOrWhiteSpace(record.DraftJson))
            throw new LowCodeException(LowCodeErrorCodes.StateInvalid, $"实体「{code}」还没有草稿，不能发布。");

        var draft = MetadataJson.Deserialize<EntityDefinition>(record.DraftJson);
        EnsureValid(draft);

        var published = string.IsNullOrWhiteSpace(record.PublishedJson)
            ? null
            : MetadataJson.Deserialize<EntityDefinition>(record.PublishedJson);

        // 不可逆的变更先挡掉，再谈要不要生成 DDL——否则会白生成一批永远执行不了的脚本。
        if (published is not null) SchemaCompatibility.EnsureCompatible(published, draft);

        // 已经有待执行的变更：不要重复生成，直接把现有的一批交回去。
        var pending = await store.ListPendingChangesAsync(code, cancellationToken);
        if (pending.Count > 0) return PublishResult.Blocked(pending);

        // 以库为准做 diff：DBA 可能已经手工执行过，也可能只执行了一半。
        var shape = await inspector.InspectAsync(draft.TableName, cancellationToken);
        var planned = planner.Plan(draft, shape);
        if (planned.Count > 0)
        {
            var changes = planned.Select(change => new SchemaChangeRecord
            {
                Id = Guid.NewGuid(),
                AppCode = appCode,
                EntityCode = code,
                Kind = change.Kind,
                Status = SchemaChangeStatus.Pending,
                PlanJson = change.PlanJson,
                ScriptSql = change.Script,
                CreatedAtUtc = _clock.GetUtcNow(),
                CreatedByUserId = userId
            }).ToList();

            await store.AddChangesAsync(changes, cancellationToken);
            await store.SaveChangesAsync(cancellationToken);
            return PublishResult.Blocked(changes);
        }

        var version = record.PublishedVersion + 1;

        await store.AddVersionAsync(new MetaVersionRecord
        {
            Id = Guid.NewGuid(),
            OwnerType = EntityOwnerType,
            OwnerCode = code,
            Version = version,
            PayloadJson = record.DraftJson,
            CreatedAtUtc = _clock.GetUtcNow(),
            CreatedByUserId = userId
        }, cancellationToken);

        record.PublishedJson = record.DraftJson;
        record.PublishedVersion = version;
        record.Status = MetadataStatus.Active;
        record.UpdatedAtUtc = _clock.GetUtcNow();
        record.UpdatedByUserId = userId;

        await store.SaveChangesAsync(cancellationToken);
        return PublishResult.Done(version);
    }

    public async Task<IReadOnlyList<MetaVersionRecord>> ListVersionsAsync(
        string appCode, string code, CancellationToken cancellationToken)
    {
        await RequireEntityAsync(appCode, code, cancellationToken);
        return await store.ListVersionsAsync(EntityOwnerType, code, cancellationToken);
    }

    /// <summary>
    /// 回滚 = 把历史版本写回草稿。不直接改线上，
    /// 因为回滚往往伴随结构变化，必须再走一次发布把 DDL 带出来。
    /// </summary>
    public async Task<EntityDefinition> RollbackAsync(
        string appCode, string code, int version, Guid? userId, CancellationToken cancellationToken)
    {
        var record = await RequireEntityAsync(appCode, code, cancellationToken);
        var versions = await store.ListVersionsAsync(EntityOwnerType, code, cancellationToken);
        var target = versions.FirstOrDefault(item => item.Version == version)
                     ?? throw new LowCodeException(LowCodeErrorCodes.NotFound, $"实体「{code}」不存在版本 {version}。");

        var restored = MetadataJson.Deserialize<EntityDefinition>(target.PayloadJson);

        record.DraftJson = target.PayloadJson;
        // 名称是冗余列（列表页要用），必须跟着 JSON 一起回退，否则列表显示的是回滚前的名字。
        record.Name = restored.Name;
        record.NameEn = restored.NameEn;
        record.Revision += 1;
        record.UpdatedAtUtc = _clock.GetUtcNow();
        record.UpdatedByUserId = userId;

        await store.SaveChangesAsync(cancellationToken);
        return restored;
    }

    private async Task<MetaEntityRecord> RequireEntityAsync(
        string appCode, string code, CancellationToken cancellationToken)
    {
        var record = await store.FindEntityAsync(appCode, code, cancellationToken);
        return record ?? throw new LowCodeException(LowCodeErrorCodes.NotFound, $"实体「{code}」不存在。");
    }

    private static void EnsureValid(EntityDefinition definition)
    {
        var errors = EntityDefinitionValidator.Validate(definition);
        if (errors.Count == 0) return;

        var detail = string.Join("；", errors.Select(error => $"{error.Path} {error.Message}"));
        throw new LowCodeException(LowCodeErrorCodes.ValidationFailed, detail);
    }
}
