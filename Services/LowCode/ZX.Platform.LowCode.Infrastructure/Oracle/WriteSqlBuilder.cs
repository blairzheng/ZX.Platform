using System.Globalization;
using ZX.Platform.LowCode.Domain;

namespace ZX.Platform.LowCode.Infrastructure.Oracle;

/// <summary>写入上下文：谁、什么时候、在哪个 SBU。</summary>
public sealed record WriteContext(Guid? UserId, Guid? SbuId, DateTimeOffset Now);

public sealed class InsertRequest
{
    public required EntityDefinition Entity { get; init; }
    public required string Schema { get; init; }
    public required Guid Id { get; init; }
    public required WriteContext Context { get; init; }
    public IReadOnlyDictionary<string, object?> Values { get; init; } =
        new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
}

public sealed class UpdateRequest
{
    public required EntityDefinition Entity { get; init; }
    public required string Schema { get; init; }
    public required Guid Id { get; init; }
    public required int Revision { get; init; }
    public required WriteContext Context { get; init; }
    public IReadOnlyDictionary<string, object?> Values { get; init; } =
        new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// 写入语句生成。三件事只在这里发生：保留列由服务端赋值、乐观锁带 revision、归属不可改。
/// </summary>
public static class WriteSqlBuilder
{
    public static SqlQuery BuildInsert(InsertRequest request)
    {
        var entity = request.Entity;
        var parameters = new List<SqlParameter>();
        var columns = new List<string>();
        var values = new List<string>();

        void Add(string column, object? value)
        {
            var name = $"p{parameters.Count}";
            columns.Add(OracleIdentifier.Quote(column));
            values.Add($":{name}");
            parameters.Add(new SqlParameter(name, value));
        }

        Add(ReservedColumns.Id, request.Id);

        if (entity.HasSbuScope)
        {
            if (request.Context.SbuId is null)
                throw LowCodeException.SbuRequired($"实体「{entity.Code}」要求选择 SBU。");

            Add(ReservedColumns.SbuId, request.Context.SbuId);
        }

        foreach (var field in entity.ActiveFields.OrderBy(field => field.Sort))
        {
            var value = Resolve(field, request.Values, request.Context);
            if (value is null && field.Required)
                throw new LowCodeException(LowCodeErrorCodes.ValidationFailed, $"字段「{field.Key}」必填。");

            Add(field.ColumnName, value);
        }

        Add(ReservedColumns.CreatedAtUtc, request.Context.Now);
        Add(ReservedColumns.CreatedByUserId, request.Context.UserId);
        Add(ReservedColumns.UpdatedAtUtc, request.Context.Now);
        Add(ReservedColumns.UpdatedByUserId, request.Context.UserId);
        Add(ReservedColumns.IsDeleted, 0);
        Add(ReservedColumns.Revision, 1);

        var table = OracleIdentifier.QuoteQualified(request.Schema, entity.TableName);
        var sql = $"INSERT INTO {table} ({string.Join(",", columns)}) VALUES ({string.Join(",", values)})";

        return new SqlQuery(sql, parameters);
    }

    public static SqlQuery BuildUpdate(UpdateRequest request)
    {
        var entity = request.Entity;
        var parameters = new List<SqlParameter>();
        var assignments = new List<string>();

        void Add(string column, object? value)
        {
            var name = $"p{parameters.Count}";
            assignments.Add($"{OracleIdentifier.Quote(column)} = :{name}");
            parameters.Add(new SqlParameter(name, value));
        }

        foreach (var (key, raw) in request.Values)
        {
            // 保留列不接受客户端写入：归属由服务端赋值，审计与版本由引擎维护。
            if (ReservedColumns.IsReserved(key))
            {
                if (string.Equals(key, ReservedColumns.SbuId, StringComparison.OrdinalIgnoreCase))
                    throw new LowCodeException(LowCodeErrorCodes.Forbidden, "不允许通过编辑修改数据归属。");

                throw new LowCodeException(LowCodeErrorCodes.ValidationFailed, $"保留列「{key}」不能直接写入。");
            }

            var field = entity.FindField(key);
            if (field is null)
                throw new LowCodeException(LowCodeErrorCodes.ValidationFailed, $"未知字段「{key}」。");

            if (field.Disabled)
                throw new LowCodeException(LowCodeErrorCodes.ValidationFailed, $"字段「{key}」已停用。");

            var value = Coerce(field, raw);
            if (value is null && field.Required)
                throw new LowCodeException(LowCodeErrorCodes.ValidationFailed, $"字段「{key}」必填。");

            Add(field.ColumnName, value);
        }

        if (assignments.Count == 0)
            throw new LowCodeException(LowCodeErrorCodes.ValidationFailed, "没有需要更新的字段。");

        Add(ReservedColumns.UpdatedAtUtc, request.Context.Now);
        Add(ReservedColumns.UpdatedByUserId, request.Context.UserId);
        assignments.Add($"{OracleIdentifier.Quote(ReservedColumns.Revision)} = {OracleIdentifier.Quote(ReservedColumns.Revision)} + 1");

        var idName = $"p{parameters.Count}";
        parameters.Add(new SqlParameter(idName, request.Id));
        var revisionName = $"p{parameters.Count}";
        parameters.Add(new SqlParameter(revisionName, request.Revision));

        var table = OracleIdentifier.QuoteQualified(request.Schema, entity.TableName);
        var sql = $"UPDATE {table} SET {string.Join(",", assignments)} "
                  + $"WHERE {OracleIdentifier.Quote(ReservedColumns.Id)} = :{idName} "
                  + $"AND {OracleIdentifier.Quote(ReservedColumns.Revision)} = :{revisionName} "
                  + $"AND {OracleIdentifier.Quote(ReservedColumns.IsDeleted)} = 0";

        return new SqlQuery(sql, parameters);
    }

    public static SqlQuery BuildSoftDelete(EntityDefinition entity, string schema, Guid id, int revision, WriteContext context)
    {
        var table = OracleIdentifier.QuoteQualified(schema, entity.TableName);
        var sql = $"UPDATE {table} SET "
                  + $"{OracleIdentifier.Quote(ReservedColumns.IsDeleted)} = 1, "
                  + $"{OracleIdentifier.Quote(ReservedColumns.Revision)} = {OracleIdentifier.Quote(ReservedColumns.Revision)} + 1, "
                  + $"{OracleIdentifier.Quote(ReservedColumns.UpdatedAtUtc)} = :now, "
                  + $"{OracleIdentifier.Quote(ReservedColumns.UpdatedByUserId)} = :user "
                  + $"WHERE {OracleIdentifier.Quote(ReservedColumns.Id)} = :id "
                  + $"AND {OracleIdentifier.Quote(ReservedColumns.Revision)} = :rev "
                  + $"AND {OracleIdentifier.Quote(ReservedColumns.IsDeleted)} = 0";

        return new SqlQuery(sql,
        [
            new SqlParameter("now", context.Now),
            new SqlParameter("user", context.UserId),
            new SqlParameter("id", id),
            new SqlParameter("rev", revision)
        ]);
    }

    private static object? Resolve(
        FieldDefinition field,
        IReadOnlyDictionary<string, object?> values,
        WriteContext context)
    {
        if (TryGet(values, field.Key, out var value) && value is not null)
            return Coerce(field, value);

        if (field.HasDefault) return ResolveDefault(field, context);

        return null;
    }

    private static object? ResolveDefault(FieldDefinition field, WriteContext context)
    {
        var raw = field.DefaultValue!;

        if (raw == FieldValueParser.NowToken) return context.Now;
        if (raw == FieldValueParser.CurrentUserIdToken) return context.UserId;

        return FieldValueParser.TryParseValue(field.Type, raw, field.Options, out var parsed)
            ? Coerce(field, parsed)
            : null;
    }

    /// <summary>boolean 在 Oracle 里是 NUMBER(1)，Dapper 绑 bool 会走错类型。</summary>
    private static object? Coerce(FieldDefinition field, object? value)
    {
        if (value is null) return null;

        return field.Type switch
        {
            FieldType.Boolean => value switch
            {
                bool flag => flag ? 1 : 0,
                string text when bool.TryParse(text, out var parsed) => parsed ? 1 : 0,
                _ => value
            },
            FieldType.Number or FieldType.Amount or FieldType.Percent => value switch
            {
                decimal number => number,
                string text when decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) => parsed,
                _ => value
            },
            _ => value
        };
    }

    private static bool TryGet(IReadOnlyDictionary<string, object?> values, string key, out object? value)
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
