using System.Globalization;
using ZX.Platform.LowCode.Domain;

namespace ZX.Platform.LowCode.Infrastructure.Oracle;

/// <summary>
/// DDL 生成。所有语句都是幂等可读的纯文本，交给 SchemaChange 记录与 DBA 执行。
/// 两条硬规则：标识符全部带引号；删表一律带 PURGE（Oracle 默认进回收站，等于删两次）。
/// </summary>
public static class DdlSqlBuilder
{
    public static IReadOnlyList<string> CreateTable(EntityDefinition entity, string schema)
    {
        EnsureTableName(entity);
        var statements = new List<string> { CreateTableStatement(entity, schema) };
        if (entity.HasSbuScope)
            statements.Add(CreateIndex(entity, schema, ReservedColumns.SbuId, unique: false));

        foreach (var field in entity.ActiveFields.OrderBy(field => field.Sort))
        {
            if (field.Unique)
                statements.AddRange(CreateUniqueIndex(entity, schema, field));
            else if (field.Indexed || field.Type == FieldType.Relation)
                statements.Add(CreateIndex(entity, schema, field.ColumnName, unique: false));
        }

        return statements;
    }

    private static string CreateTableStatement(EntityDefinition entity, string schema)
    {
        var lines = new List<string>
        {
            $"  {Q(ReservedColumns.Id)} {OracleTypeMapper.IdType} NOT NULL"
        };

        if (entity.HasSbuScope)
            lines.Add($"  {Q(ReservedColumns.SbuId)} {OracleTypeMapper.IdType}");

        foreach (var field in entity.ActiveFields.OrderBy(field => field.Sort))
            lines.Add($"  {ColumnClause(field)}");

        lines.Add($"  {Q(ReservedColumns.CreatedAtUtc)} {OracleTypeMapper.TimestampType} NOT NULL");
        lines.Add($"  {Q(ReservedColumns.CreatedByUserId)} {OracleTypeMapper.IdType}");
        lines.Add($"  {Q(ReservedColumns.UpdatedAtUtc)} {OracleTypeMapper.TimestampType}");
        lines.Add($"  {Q(ReservedColumns.UpdatedByUserId)} {OracleTypeMapper.IdType}");
        lines.Add($"  {Q(ReservedColumns.IsDeleted)} {OracleTypeMapper.BooleanType} DEFAULT 0 NOT NULL");
        lines.Add($"  {Q(ReservedColumns.Revision)} {OracleTypeMapper.RevisionType} DEFAULT 1 NOT NULL");
        lines.Add($"  CONSTRAINT {QS(Naming.PrimaryKeyName(entity.TableName))} PRIMARY KEY ({Q(ReservedColumns.Id)})");

        return $"CREATE TABLE {OracleIdentifier.QuoteQualified(schema, entity.TableName)} (\n"
               + string.Join(",\n", lines)
               + "\n)";
    }

    /// <summary>
    /// 加列。新增字段必须可空，或同时给默认值——否则已有行无法满足 NOT NULL。
    /// </summary>
    public static IReadOnlyList<string> AddColumn(EntityDefinition entity, string schema, FieldDefinition field)
    {
        EnsureTableName(entity);

        if (field.Required && !field.HasDefault)
            throw new InvalidOperationException(
                $"新增字段「{field.Key}」必须可空，或提供 defaultValue。");

        var statements = new List<string>
        {
            $"ALTER TABLE {OracleIdentifier.QuoteQualified(schema, entity.TableName)} ADD ({ColumnClause(field)})"
        };

        if (field.Unique)
            statements.AddRange(CreateUniqueIndex(entity, schema, field));
        else if (field.Indexed || field.Type == FieldType.Relation)
            statements.Add(CreateIndex(entity, schema, field.ColumnName, unique: false));

        return statements;
    }

    /// <summary>退役时的物理清理。永远带 PURGE，不给"删一半留在回收站"的机会。</summary>
    public static string DropTable(EntityDefinition entity, string schema)
    {
        EnsureTableName(entity);
        return $"DROP TABLE {OracleIdentifier.QuoteQualified(schema, entity.TableName)} PURGE";
    }

    /// <summary>
    /// 表名不允许截断——它同时出现在 DDL、查询和迁移里，截断会导致三处不一致。
    /// 超长由校验器拦下，这里只是最后一道保险。
    /// </summary>
    private static void EnsureTableName(EntityDefinition entity)
    {
        if (!Naming.IsValidTableName(entity.TableName))
            throw new InvalidOperationException(
                $"物理表名 {entity.TableName} 超过 {OracleIdentifier.MaxLength} 字符，请缩短应用或实体编码。");
    }

    private static string ColumnClause(FieldDefinition field)
    {
        var type = OracleTypeMapper.ColumnType(field);
        var defaultSql = TryRenderDefault(field, out var literal) ? $" DEFAULT {literal}" : string.Empty;
        var nullability = field.Required ? "NOT NULL" : "NULL";
        return $"{Q(field.ColumnName)} {type}{defaultSql} {nullability}";
    }

    private static string CreateIndex(EntityDefinition entity, string schema, string column, bool unique)
    {
        var indexName = OracleIdentifier.Shorten(
            unique
                ? Naming.UniqueIndexName(entity.TableName, column)
                : Naming.IndexName(entity.TableName, column));

        return $"CREATE {(unique ? "UNIQUE " : string.Empty)}INDEX {Q(indexName)} "
               + $"ON {OracleIdentifier.QuoteQualified(schema, entity.TableName)} ({Q(column)}) ONLINE";
    }

    /// <summary>
    /// Oracle 不支持部分索引，用虚拟列 + 唯一索引模拟"删了还能同名"。
    /// 与 Identity 的 MENU 表做法一致。
    /// </summary>
    private static IReadOnlyList<string> CreateUniqueIndex(EntityDefinition entity, string schema, FieldDefinition field)
    {
        if (OracleTypeMapper.IsClob(field.Type))
            throw new InvalidOperationException($"字段「{field.Key}」是 CLOB，不能建唯一索引。");

        var virtualColumn = OracleIdentifier.Shorten($"ACTIVE_{field.ColumnName}");
        var table = OracleIdentifier.QuoteQualified(schema, entity.TableName);

        return
        [
            $"ALTER TABLE {table} ADD ({Q(virtualColumn)} {OracleTypeMapper.VirtualColumnType(field)} "
            + $"GENERATED ALWAYS AS (CASE WHEN {Q(ReservedColumns.IsDeleted)} = 0 THEN {Q(field.ColumnName)} END) VIRTUAL)",

            $"CREATE UNIQUE INDEX {QS(Naming.UniqueIndexName(entity.TableName, field.ColumnName))} "
            + $"ON {table} ({Q(virtualColumn)}) ONLINE"
        ];
    }

    /// <summary>
    /// 只在能安全内联时渲染默认值。日期、CLOB、JSON 数组交给应用层，
    /// 避免 DDL 里出现依赖会话 NLS 的字面量。
    /// </summary>
    private static bool TryRenderDefault(FieldDefinition field, out string literal)
    {
        literal = string.Empty;
        if (!field.HasDefault) return false;

        var raw = field.DefaultValue!;
        if (FieldValueParser.IsSpecial(raw)) return false;

        switch (field.Type)
        {
            case FieldType.Boolean:
                if (!bool.TryParse(raw, out var flag)) return false;
                literal = flag ? "1" : "0";
                return true;

            case FieldType.Number:
            case FieldType.Amount:
            case FieldType.Percent:
                if (!decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var number))
                    return false;
                literal = number.ToString(CultureInfo.InvariantCulture);
                return true;

            case FieldType.Text:
            case FieldType.Select:
            case FieldType.AutoNumber:
                literal = $"'{raw.Replace("'", "''")}'";
                return true;

            default:
                return false;
        }
    }

    private static string Q(string identifier) => OracleIdentifier.Quote(identifier);

    private static string QS(string identifier) => OracleIdentifier.QuoteShortened(identifier);
}
