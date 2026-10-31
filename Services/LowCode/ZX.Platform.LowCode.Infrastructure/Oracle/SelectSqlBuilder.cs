using System.Text;
using ZX.Platform.LowCode.Domain;

namespace ZX.Platform.LowCode.Infrastructure.Oracle;

/// <summary>
/// 只读查询生成。三条护栏写死在这里：SBU 过滤、算子白名单、分页上限。
/// 任何一条都不允许由页面配置关闭。
///
/// 不带 schema 前缀：表建在哪个 schema 由连接账号决定，见 docs/lowcode/setup.md。
/// </summary>
public static class SelectSqlBuilder
{
    public const int MaxPageSize = 200;
    public const int MaxSkip = 10_000;
    public const int MaxInValues = 1000;

    /// <summary>
    /// 实际取多少条。截断规则只有这一处，读结果分页信息时也用它，
    /// 否则"生成的 SQL"和"返回给前端的 take"会各算各的。
    /// </summary>
    public static int ResolveTake(int take) =>
        Math.Clamp(take <= 0 ? ViewDefinition.DefaultPageSize : take, 1, MaxPageSize);

    public static SqlQuery BuildSelect(QuerySpec request)
    {
        var context = new BuildContext(request);
        var where = BuildWhere(request, context);
        var columns = BuildProjection(request);
        var order = BuildOrderBy(request);
        var table = OracleIdentifier.Quote(request.Entity.TableName);

        var take = ResolveTake(request.Take);
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

    public static SqlQuery BuildCount(QuerySpec request)
    {
        var context = new BuildContext(request);
        var where = BuildWhere(request, context);
        var table = OracleIdentifier.Quote(request.Entity.TableName);

        var sql = $"SELECT COUNT(*) FROM {table}" + (where.Length > 0 ? $" WHERE {where}" : string.Empty);
        return new SqlQuery(sql, context.Parameters);
    }

    private static string BuildProjection(QuerySpec request)
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

    private static string BuildWhere(QuerySpec request, BuildContext context)
    {
        var clauses = new List<string> { $"{OracleIdentifier.Quote(ReservedColumns.IsDeleted)} = 0" };

        if (request.Id is not null)
        {
            clauses.Add($"{OracleIdentifier.Quote(ReservedColumns.Id)} = :recordId");
            context.Parameters.Add(new SqlParameter("recordId", request.Id));
        }

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

    private static string BuildOrderBy(QuerySpec request)
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

    private sealed class BuildContext(QuerySpec request)
    {
        public List<SqlParameter> Parameters { get; } = [];

        public QuerySpec Request { get; } = request;
    }
}
