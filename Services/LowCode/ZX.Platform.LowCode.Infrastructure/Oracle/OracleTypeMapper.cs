using ZX.Platform.LowCode.Domain;

namespace ZX.Platform.LowCode.Infrastructure.Oracle;

/// <summary>
/// 字段类型 → Oracle 列型。这张映射是平台最底层的契约，改动成本高于 schema 版本。
/// </summary>
public static class OracleTypeMapper
{
    public const string TimestampType = "TIMESTAMP(7) WITH TIME ZONE";
    public const string IdType = "RAW(16)";
    public const string BooleanType = "NUMBER(1)";
    public const string RevisionType = "NUMBER(10)";

    public static string ColumnType(FieldDefinition field) => field.Type switch
    {
        FieldType.Text => $"NVARCHAR2({field.EffectiveMaxLength})",
        FieldType.LongText => "CLOB",
        FieldType.Number => $"NUMBER({field.EffectivePrecision},0)",
        FieldType.Amount => "NUMBER(18,4)",
        FieldType.Percent => "NUMBER(9,4)",
        FieldType.Date => "DATE",
        FieldType.DateTime => TimestampType,
        FieldType.Boolean => BooleanType,
        FieldType.Select => $"NVARCHAR2({FieldDefinition.MaxOptionValueLength})",
        FieldType.MultiSelect => "NVARCHAR2(2000)",
        FieldType.Relation => IdType,
        FieldType.Attachment => "CLOB",
        FieldType.AutoNumber => "NVARCHAR2(40)",
        _ => throw new ArgumentOutOfRangeException(nameof(field), field.Type, "未知字段类型。")
    };

    /// <summary>
    /// 唯一索引用的虚拟列类型。必须与源列同型，否则 Oracle 会拒绝生成表达式。
    /// CLOB 不能出现在虚拟列里，所以 longtext / attachment 不允许 unique。
    /// </summary>
    public static string VirtualColumnType(FieldDefinition field) => ColumnType(field);

    public static string ReservedColumnType(string column) => column.ToUpperInvariant() switch
    {
        ReservedColumns.Id => IdType,
        ReservedColumns.SbuId => IdType,
        ReservedColumns.CreatedAtUtc => TimestampType,
        ReservedColumns.CreatedByUserId => IdType,
        ReservedColumns.UpdatedAtUtc => TimestampType,
        ReservedColumns.UpdatedByUserId => IdType,
        ReservedColumns.IsDeleted => BooleanType,
        ReservedColumns.Revision => RevisionType,
        _ => throw new ArgumentOutOfRangeException(nameof(column), column, "未知保留列。")
    };

    public static bool IsClob(FieldType type) => type is FieldType.LongText or FieldType.Attachment;
}
