using ZX.Platform.LowCode.Application.Metadata;
using ZX.Platform.LowCode.Domain;

namespace ZX.Platform.LowCode.Application.Runtime;

/// <summary>
/// 列表查询。只读，不碰写入。
///
/// 它存在的意义不是"转发"——而是保证每一次查询都满足三条：
/// 1. 只用**已发布**的元数据，草稿态的定义永远查不到；
/// 2. 数据范围（SBU）来自会话，不从请求体里读；
/// 3. 返回的值已经按字段类型规范化，前端拿到的 JSON 只有一种形状。
/// </summary>
public sealed class QueryEngine(MetadataService metadata, IRecordGateway gateway)
{
    public async Task<RecordPage> QueryAsync(
        string appCode,
        string entityCode,
        RecordQueryRequest request,
        RecordScope scope,
        CancellationToken cancellationToken)
    {
        var entity = await RequirePublishedAsync(appCode, entityCode, cancellationToken);

        var spec = new QuerySpec
        {
            Entity = entity,
            Fields = request.Fields,
            Filters = request.Filters,
            Sorts = request.Sorts,
            Skip = request.Skip,
            Take = request.Take,
            SbuId = scope.SbuId,
            AllSbus = scope.AllSbus
        };

        var page = await gateway.QueryAsync(spec, cancellationToken);
        if (page.Rows.Count == 0) return page;

        return page with
        {
            Rows = [.. page.Rows.Select(row => RecordValueMapper.ToWire(entity, row))]
        };
    }

    /// <summary>
    /// 单条读取。走列表查询同一条路径，只是把主键当成筛选条件，
    /// 这样 SBU 过滤与软删过滤不会出现第二套实现。
    /// </summary>
    public async Task<IReadOnlyDictionary<string, object?>?> FindAsync(
        string appCode,
        string entityCode,
        Guid id,
        RecordScope scope,
        CancellationToken cancellationToken)
    {
        var entity = await RequirePublishedAsync(appCode, entityCode, cancellationToken);

        var page = await gateway.QueryAsync(new QuerySpec
        {
            Entity = entity,
            Id = id,
            Take = 1,
            SbuId = scope.SbuId,
            AllSbus = scope.AllSbus
        }, cancellationToken);

        var row = page.Rows.FirstOrDefault();
        return row is null ? null : RecordValueMapper.ToWire(entity, row);
    }

    private Task<EntityDefinition> RequirePublishedAsync(
        string appCode, string entityCode, CancellationToken cancellationToken) =>
        metadata.RequirePublishedAsync(appCode, entityCode, cancellationToken);
}
