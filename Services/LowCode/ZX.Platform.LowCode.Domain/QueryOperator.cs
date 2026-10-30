namespace ZX.Platform.LowCode.Domain;

/// <summary>查询算子。只允许出现这里列出的算子，前端与后端共用同一份定义。</summary>
public enum QueryOperator
{
    Eq,
    Ne,
    Gt,
    Ge,
    Lt,
    Le,
    Like,
    In,
    Between,
    IsNull,
    Contains
}

public static class QueryOperators
{
    private static readonly Dictionary<QueryOperator, string> WireNames = new()
    {
        [QueryOperator.Eq] = "eq",
        [QueryOperator.Ne] = "ne",
        [QueryOperator.Gt] = "gt",
        [QueryOperator.Ge] = "ge",
        [QueryOperator.Lt] = "lt",
        [QueryOperator.Le] = "le",
        [QueryOperator.Like] = "like",
        [QueryOperator.In] = "in",
        [QueryOperator.Between] = "between",
        [QueryOperator.IsNull] = "isNull",
        [QueryOperator.Contains] = "contains"
    };

    private static readonly Dictionary<string, QueryOperator> ByWireName =
        WireNames.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.OrdinalIgnoreCase);

    public static string ToWireName(this QueryOperator op) => WireNames[op];

    public static bool TryParse(string? value, out QueryOperator op)
    {
        op = default;
        return !string.IsNullOrWhiteSpace(value) && ByWireName.TryGetValue(value, out op);
    }
}

/// <summary>
/// 类型与算子的合法组合。表外的组合一律 400 / 610003，不接受"尽量支持"。
/// </summary>
public static class FieldTypeRules
{
    private static readonly IReadOnlyDictionary<FieldType, IReadOnlySet<QueryOperator>> AllowedOperators =
        new Dictionary<FieldType, IReadOnlySet<QueryOperator>>
        {
            [FieldType.Text] = Set(QueryOperator.Eq, QueryOperator.Like, QueryOperator.In, QueryOperator.IsNull),
            [FieldType.LongText] = Set(QueryOperator.IsNull),
            [FieldType.Number] = Set(QueryOperator.Eq, QueryOperator.Ne, QueryOperator.Gt, QueryOperator.Ge,
                QueryOperator.Lt, QueryOperator.Le, QueryOperator.Between, QueryOperator.In),
            [FieldType.Amount] = Set(QueryOperator.Eq, QueryOperator.Ne, QueryOperator.Gt, QueryOperator.Ge,
                QueryOperator.Lt, QueryOperator.Le, QueryOperator.Between, QueryOperator.In),
            [FieldType.Percent] = Set(QueryOperator.Eq, QueryOperator.Ne, QueryOperator.Gt, QueryOperator.Ge,
                QueryOperator.Lt, QueryOperator.Le, QueryOperator.Between, QueryOperator.In),
            [FieldType.Date] = Set(QueryOperator.Eq, QueryOperator.Ge, QueryOperator.Le, QueryOperator.Between),
            [FieldType.DateTime] = Set(QueryOperator.Eq, QueryOperator.Ge, QueryOperator.Le, QueryOperator.Between),
            [FieldType.Boolean] = Set(QueryOperator.Eq),
            [FieldType.Select] = Set(QueryOperator.Eq, QueryOperator.In),
            [FieldType.MultiSelect] = Set(QueryOperator.Contains),
            [FieldType.Relation] = Set(QueryOperator.Eq, QueryOperator.In),
            [FieldType.Attachment] = Set(),
            [FieldType.AutoNumber] = Set(QueryOperator.Eq, QueryOperator.Like, QueryOperator.In)
        };

    /// <summary>不可排序的类型：CLOB 不能排序，JSON 数组排序没有意义。</summary>
    private static readonly IReadOnlySet<FieldType> NotSortable = new HashSet<FieldType>
    {
        FieldType.LongText,
        FieldType.MultiSelect,
        FieldType.Attachment
    };

    /// <summary>可建普通索引的类型：CLOB 不能直接建索引。</summary>
    private static readonly IReadOnlySet<FieldType> NotIndexable = new HashSet<FieldType>
    {
        FieldType.LongText,
        FieldType.Attachment
    };

    public static IReadOnlySet<QueryOperator> Operators(FieldType type) => AllowedOperators[type];

    public static bool Supports(FieldType type, QueryOperator op) => AllowedOperators[type].Contains(op);

    public static bool SupportsAnyFilter(FieldType type) => AllowedOperators[type].Count > 0;

    public static bool Sortable(FieldType type) => !NotSortable.Contains(type);

    public static bool Indexable(FieldType type) => !NotIndexable.Contains(type);

    private static IReadOnlySet<QueryOperator> Set(params QueryOperator[] operators) =>
        new HashSet<QueryOperator>(operators);
}
