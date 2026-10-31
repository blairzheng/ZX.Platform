namespace ZX.Platform.LowCode.Domain;

/// <summary>一个筛选条件。In / Between 用 Values，其余用 Value。</summary>
public sealed record FilterCondition(
    string Field,
    QueryOperator Operator,
    string? Value = null,
    IReadOnlyList<string>? Values = null);

/// <summary>
/// 一次列表查询的语义描述：查哪个实体、取哪些字段、怎么筛、怎么排、翻到第几页。
///
/// 刻意不含 schema 和 SQL：这是"要什么"，不是"怎么查"。
/// 渲染成 Oracle SQL 是 Infrastructure 的事，换成别的库时这个类型不用动。
/// </summary>
public sealed class QuerySpec
{
    public required EntityDefinition Entity { get; init; }

    /// <summary>按主键取单条。主键是保留列，不是实体字段，所以单独开一个入口。</summary>
    public Guid? Id { get; init; }

    /// <summary>投影字段。null 表示全部启用字段。</summary>
    public IReadOnlyList<string>? Fields { get; init; }

    public IReadOnlyList<FilterCondition> Filters { get; init; } = [];

    public IReadOnlyList<ViewSort> Sorts { get; init; } = [];

    public int Skip { get; init; }

    public int Take { get; init; } = ViewDefinition.DefaultPageSize;

    /// <summary>当前 SBU。实体要求 SBU 时必须提供。</summary>
    public Guid? SbuId { get; init; }

    /// <summary>是否拥有 read.all 权限，拥有时跳过 SBU 过滤。</summary>
    public bool AllSbus { get; init; }
}

/// <summary>一页查询结果。Total 是满足条件的总数，不是本页条数。</summary>
public sealed record RecordPage(
    IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows,
    int Total,
    int Skip,
    int Take)
{
    public static RecordPage Empty(int skip, int take) => new([], 0, skip, take);
}
