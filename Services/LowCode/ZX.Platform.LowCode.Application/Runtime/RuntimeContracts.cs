using ZX.Platform.LowCode.Domain;

namespace ZX.Platform.LowCode.Application.Runtime;

/// <summary>
/// 一次读写所处的数据范围。由 API 层根据当前登录会话与权限算出来，引擎自己不去猜。
/// </summary>
public sealed record RecordScope(Guid? UserId, Guid? SbuId, bool AllSbus)
{
    /// <summary>没有 SBU 概念的实体（scopeMode = none）用这个。</summary>
    public static RecordScope Unscoped(Guid? userId) => new(userId, null, true);
}

/// <summary>
/// 列表查询的入参，直接由前端 JSON 绑定。
/// 只描述"要什么"，不做任何校验——字段是否存在、算子是否合法由引擎和 SQL 生成器判定。
/// </summary>
public sealed class RecordQueryRequest
{
    /// <summary>投影字段。null 表示全部启用字段。</summary>
    public IReadOnlyList<string>? Fields { get; init; }

    public IReadOnlyList<FilterCondition> Filters { get; init; } = [];

    public IReadOnlyList<ViewSort> Sorts { get; init; } = [];

    public int Skip { get; init; }

    public int Take { get; init; } = ViewDefinition.DefaultPageSize;
}
