using System.Text;
using ZX.Platform.LowCode.Domain;

namespace ZX.Platform.LowCode.Infrastructure.Oracle;

/// <summary>一个筛选条件。In / Between 用 Values，其余用 Value。</summary>
public sealed record FilterCondition(
    string Field,
    QueryOperator Operator,
    string? Value = null,
    IReadOnlyList<string>? Values = null);

/// <summary>一个排序项。</summary>
public sealed record SortSpec(string Field, bool Descending = false);

/// <summary>列表查询请求。所有条件在服务端校验，不信任前端传来的字段名。</summary>
public sealed class SelectRequest
{
    public required EntityDefinition Entity { get; init; }

    public required string Schema { get; init; }

    /// <summary>投影字段。null 表示全部启用字段。</summary>
    public IReadOnlyList<string>? Fields { get; init; }

    public IReadOnlyList<FilterCondition> Filters { get; init; } = [];

    public IReadOnlyList<SortSpec> Sorts { get; init; } = [];

    public int Skip { get; init; }

    public int Take { get; init; } = ViewDefinition.DefaultPageSize;

    /// <summary>当前 SBU。实体要求 SBU 时必须提供。</summary>
    public Guid? SbuId { get; init; }

    /// <summary>是否拥有 read.all 权限，拥有时跳过 SBU 过滤。</summary>
    public bool AllSbus { get; init; }
}

/// <summary>
/// 只读查询生成。三条护栏写死在这里：SBU 过滤、算子白名单、分页上限。
/// 任何一条都不允许由页面配置关闭。
/// </summary>
public static class SelectSqlBuilder
{
    public const int MaxPageSize = 200;
    public const int MaxSkip = 10_000;
    public const int MaxInValues = 1000;

    public static SqlQuery BuildSelect(SelectRequest request)
    {
        var context = new BuildContext(request);
        var where = BuildWhere(request, context);
        var columns = BuildProjection(request);
        var order = BuildOrderBy(request);
        var table = OracleIdentifier.QuoteQualified(request.Schema, request.Entity.TableName);

        var take = Math.Clamp(request.Take <= 0 ? ViewDefinition.DefaultPageSize : request.Take, 1, MaxPageSize);
        if (request.Skip < 0) throw LowCodeException.ResultTooLarge("skip 不能为负。");
        if (request.Skip > MaxSkip)
            throw LowCodeException.ResultTooLarge($"翻页过深，skip 不得超过 {MaxSkip}，请缩小筛选范围。");

        context.Parameters.Add(new SqlParameter("skip", request.Skip));
        context.Parameters.Add(new SqlParameter("take", take));

        var sql = new StringBuilder()
            .Append("SELECT ").Append(columns)
            .Append(" FROM ").Append(table)
            .Append(where.Length > 0 ? $" WHERE {where}" : string.Empty)
            .Append(order)
            .Append(" OFFSET :skip ROWS FETCH NEXT :take ROWS ONLY")
            .ToString();

        return new SqlQuery(sql, context.Parameters);
    }

    public static SqlQuery BuildCount(SelectRequest request)
    {
        var context = new BuildContext(request);
        var where = BuildWhere(request, context);
        var table = OracleIdentifier.QuoteQualified(request.Schema, request.Entity.TableName);

        var sql = $"SELECT COUNT(*) FROM {table}" + (where.Length > 0 ? $" WHERE {where}" : string.Empty);
        return new SqlQuery(sql, context.Parameters);
    }

    private static string BuildProjection(SelectRequest request)
    {
        var columns = new List<string> { OracleIdentifier.Quote(ReservedColumns.Id) };

        if (request.Fields is { Count: > 0 })
        {
            foreach (var key in request.Fields)
            {
                var field = request.Entity.FindField(key)
                            ?? throw LowCodeException.OperatorNotAllowed($"未知字段「{key}」。");
                if (field.Disabled)
                    throw LowCodeException.OperatorNotAllowed($"字段「{key}」已停用。");

                var column = OracleIdentifier.Quote(field.ColumnName);
                if (!columns.Contains(column)) columns.Add(column);
            }
        }
        else
        {
            foreach (var field in request.Entity.ActiveFields.OrderBy(field => field.Sort))
                columns.Add(OracleIdentifier.Quote(field.ColumnName));
        }

        return string.Join(",", columns);
    }

    private static string BuildWhere(SelectRequest request, BuildContext context)
    {
        var clauses = new List<string> { $"{OracleIdentifier.Quote(ReservedColumns.IsDeleted)} = 0" };

        if (request.Entity.HasSbuScope && !request.AllSbus)
        {
            if (request.SbuId is null)
                throw LowCodeException.SbuRequired($"实体「{request.Entity.Code}」要求选择 SBU。");

            clauses.Add($"{OracleIdentifier.Quote(ReservedColumns.SbuId)} = :sbuId");
            context.Parameters.Add(new SqlParameter("sbuId", request.SbuId));
        }

        foreach (var filter in request.Filters)
            clauses.Add(BuildFilter(request.Entity, filter, context));

        return string.Join(" AND ", clauses);
    }

    private static string BuildFilter(EntityDefinition entity, FilterCondition filter, BuildContext context)
    {
        var field = entity.FindField(filter.Field)
                    ?? throw LowCodeException.OperatorNotAllowed($"未知字段「{filter.Field}」。");

        if (field.Disabled)
            throw LowCodeException.OperatorNotAllowed($"字段「{filter.Field}」已停用。");

        if (!FieldTypeRules.Supports(field.Type, filter.Operator))
            throw LowCodeException.OperatorNotAllowed(
                $"{field.Type.ToWireName()} 字段「{field.Key}」不支持 {filter.Operator.ToWireName()}。");

        var column = OracleIdentifier.Quote(field.ColumnName);

        switch (filter.Operator)
        {
            case QueryOperator.IsNull:
                return $"{column} IS NULL";

            case QueryOperator.Like:
                return $"{column} LIKE {Bind(context, EscapeLike(filter.Value ?? string.Empty) + "%")} ESCAPE '\\'";

            case QueryOperator.Contains:
                return $"{column} LIKE {Bind(context, "%\"" + EscapeLike(filter.Value ?? string.Empty) + "\"%")} ESCAPE '\\'";

            case QueryOperator.In:
                return BuildIn(column, filter.Values, context);

            case QueryOperator.Between:
                if (filter.Values is not { Count: 2 })
                    throw LowCodeException.OperatorNotAllowed("between 需要两个值。");
                return $"{column} BETWEEN {Bind(context, filter.Values[0])} AND {Bind(context, filter.Values[1])}";

            default:
                return $"{column} {Comparison(filter.Operator)} {Bind(context, NormalizeValue(field, filter.Value))}";
        }
    }

    private static string BuildIn(string column, IReadOnlyList<string>? values, BuildContext context)
    {
        if (values is null || values.Count == 0) return "1 = 0";

        if (values.Count > MaxInValues)
            throw LowCodeException.ResultTooLarge($"in 条件最多 {MaxInValues} 个值。");

        var placeholders = values.Select(value => Bind(context, value)).ToArray();
        return $"{column} IN ({string.Join(",", placeholders)})";
    }

    private static string Comparison(QueryOperator op) => op switch
    {
        QueryOperator.Eq => "=",
        QueryOperator.Ne => "<>",
        QueryOperator.Gt => ">",
        QueryOperator.Ge => ">=",
        QueryOperator.Lt => "<",
        QueryOperator.Le => "<=",
        _ => throw LowCodeException.OperatorNotAllowed($"不支持的算子 {op}。")
    };

    /// <summary>boolean 在 Oracle 里是 NUMBER(1)，绑字符串会走隐式转换，这里显式转。</summary>
    private static object? NormalizeValue(FieldDefinition field, string? value)
    {
        if (value is null) return null;
        if (field.Type != FieldType.Boolean) return value;

        return bool.TryParse(value, out var flag)
            ? flag ? 1 : 0
            : throw LowCodeException.OperatorNotAllowed($"布尔字段「{field.Key}」的值必须是 true / false。");
    }

    /// <summary>转义 LIKE 的通配符。只做前缀匹配，中缀查询在这里就被消灭了。</summary>
    private static string EscapeLike(string value) => value
        .Replace("\\", "\\\\")
        .Replace("%", "\\%")
        .Replace("_", "\\_");

    private static string BuildOrderBy(SelectRequest request)
    {
        var sorts = new List<string>();

        foreach (var sort in request.Sorts)
        {
            var field = request.Entity.FindField(sort.Field)
                        ?? throw LowCodeException.OperatorNotAllowed($"未知排序字段「{sort.Field}」。");

            if (!FieldTypeRules.Sortable(field.Type))
                throw LowCodeException.OperatorNotAllowed($"{field.Type.ToWireName()} 字段「{field.Key}」不可排序。");

            sorts.Add($"{OracleIdentifier.Quote(field.ColumnName)} {(sort.Descending ? "DESC" : "ASC")}");
        }

        // 没有排序时按 ID 兜底，否则 OFFSET/FETCH 的分页结果不稳定。
        if (sorts.Count == 0)
            sorts.Add($"{OracleIdentifier.Quote(ReservedColumns.Id)} ASC");

        return " ORDER BY " + string.Join(",", sorts);
    }

    private static string Bind(BuildContext context, object? value)
    {
        var name = $"p{context.Parameters.Count}";
        context.Parameters.Add(new SqlParameter(name, value));
        return $":{name}";
    }

    private sealed class BuildContext(SelectRequest request)
    {
        public List<SqlParameter> Parameters { get; } = [];

        public SelectRequest Request { get; } = request;
    }
}
