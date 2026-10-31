using System.Globalization;
using ZX.Platform.LowCode.Application.Metadata;
using ZX.Platform.LowCode.Domain;

namespace ZX.Platform.LowCode.Application.Runtime;

/// <summary>
/// 写入。新增 / 修改 / 软删三条路径共用同一套前置检查：
///
/// 1. 只用已发布定义，草稿态写不进去；
/// 2. 请求里出现未定义或已停用的字段一律拒绝——静默丢弃会把客户端的拼写错误变成"保存成功但没生效"；
/// 3. 唯一字段先查一次再写，给出可读的 600003，数据库唯一索引作为并发下的最后一道；
/// 4. 归属（SBU）与审计字段只从会话取，请求体里的一律不认。
///
/// 唯一性预检与真实写入之间必然存在竞态，所以数据库唯一索引不能省。
/// 预检的意义只是把 ORA-00001 变成"字段「站点编码」的值「ST001」已存在"。
/// </summary>
public sealed class RecordEngine(
    MetadataService metadata,
    IRecordGateway gateway,
    TimeProvider? clock = null)
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    public async Task<Guid> CreateAsync(
        string appCode,
        string entityCode,
        IReadOnlyDictionary<string, object?> values,
        RecordScope scope,
        CancellationToken cancellationToken)
    {
        var entity = await metadata.RequirePublishedAsync(appCode, entityCode, cancellationToken);

        EnsureKeysDefined(entity, values);
        await EnsureUniqueAsync(entity, values, excludeId: null, cancellationToken);

        var id = Guid.NewGuid();
        await gateway.CreateAsync(entity, id, values, scope, _clock.GetUtcNow(), cancellationToken);
        return id;
    }

    public async Task UpdateAsync(
        string appCode,
        string entityCode,
        Guid id,
        int revision,
        IReadOnlyDictionary<string, object?> values,
        RecordScope scope,
        CancellationToken cancellationToken)
    {
        var entity = await metadata.RequirePublishedAsync(appCode, entityCode, cancellationToken);

        EnsureKeysDefined(entity, values);
        await EnsureUniqueAsync(entity, values, excludeId: id, cancellationToken);

        var affected = await gateway.UpdateAsync(
            entity, id, revision, values, scope, _clock.GetUtcNow(), cancellationToken);

        if (!affected) throw Conflict();
    }

    public async Task DeleteAsync(
        string appCode,
        string entityCode,
        Guid id,
        int revision,
        RecordScope scope,
        CancellationToken cancellationToken)
    {
        var entity = await metadata.RequirePublishedAsync(appCode, entityCode, cancellationToken);

        var affected = await gateway.DeleteAsync(
            entity, id, revision, scope, _clock.GetUtcNow(), cancellationToken);

        if (!affected) throw Conflict();
    }

    /// <summary>
    /// 未知字段与停用字段在这里拦。保留列（ID / SBU_ID / 审计 / REVISION）不在这里判——
    /// 新增时"客户端传了也不生效"是刻意行为，修改时由 WriteSqlBuilder 报 600005 / 600001。
    /// </summary>
    private static void EnsureKeysDefined(
        EntityDefinition entity,
        IReadOnlyDictionary<string, object?> values)
    {
        foreach (var key in values.Keys)
        {
            if (ReservedColumns.IsReserved(key)) continue;

            var field = entity.FindField(key)
                        ?? throw new LowCodeException(
                            LowCodeErrorCodes.ValidationFailed, $"未知字段「{key}」。");

            if (field.Disabled)
                throw new LowCodeException(
                    LowCodeErrorCodes.ValidationFailed, $"字段「{key}」已停用，不能再写入。");
        }
    }

    /// <summary>
    /// 唯一性预检。刻意**不按 SBU 过滤**：唯一索引建在整张表上，
    /// 若预检只看当前 SBU，两个 SBU 各插一条同编码时，预检都会放行，最后撞索引。
    /// </summary>
    private async Task EnsureUniqueAsync(
        EntityDefinition entity,
        IReadOnlyDictionary<string, object?> values,
        Guid? excludeId,
        CancellationToken cancellationToken)
    {
        foreach (var field in entity.ActiveFields.Where(item => item.Unique))
        {
            if (!TryGet(values, field.Key, out var raw) || raw is null) continue;

            var text = raw as string ?? Convert.ToString(raw, CultureInfo.InvariantCulture);
            if (string.IsNullOrEmpty(text)) continue;

            var page = await gateway.QueryAsync(new QuerySpec
            {
                Entity = entity,
                AllSbus = true,
                Take = 1,
                Fields = [field.Key],
                Filters = [new FilterCondition(field.Key, QueryOperator.Eq, text)]
            }, cancellationToken);

            var existing = FirstId(page);
            if (existing is not null && existing != excludeId)
                throw new LowCodeException(
                    LowCodeErrorCodes.Conflict, $"字段「{field.Name}」的值「{text}」已存在。");
        }
    }

    private static Guid? FirstId(RecordPage page)
    {
        foreach (var row in page.Rows)
        {
            if (row.TryGetValue(ReservedColumns.Id, out var value) && value is Guid id) return id;
        }

        return null;
    }

    private static LowCodeException Conflict() =>
        new(LowCodeErrorCodes.Conflict, "记录已被他人修改或已删除，请刷新后重试。");

    private static bool TryGet(
        IReadOnlyDictionary<string, object?> values, string key, out object? value)
    {
        if (values.TryGetValue(key, out value)) return true;

        foreach (var pair in values)
        {
            if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                value = pair.Value;
                return true;
            }
        }

        value = null;
        return false;
    }
}
