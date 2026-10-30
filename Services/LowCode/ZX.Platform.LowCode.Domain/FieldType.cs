namespace ZX.Platform.LowCode.Domain;

/// <summary>
/// 字段类型。这 13 种同时决定物理列型、前端控件、校验规则、查询算子，只增不改。
/// 新增类型的成本高于元数据 schema 版本升级，见 docs/lowcode/field-types.md。
/// </summary>
public enum FieldType
{
    Text,
    LongText,
    Number,
    Amount,
    Percent,
    Date,
    DateTime,
    Boolean,
    Select,
    MultiSelect,
    Relation,
    Attachment,
    AutoNumber
}

public static class FieldTypes
{
    private static readonly Dictionary<FieldType, string> WireNames = new()
    {
        [FieldType.Text] = "text",
        [FieldType.LongText] = "longtext",
        [FieldType.Number] = "number",
        [FieldType.Amount] = "amount",
        [FieldType.Percent] = "percent",
        [FieldType.Date] = "date",
        [FieldType.DateTime] = "datetime",
        [FieldType.Boolean] = "boolean",
        [FieldType.Select] = "select",
        [FieldType.MultiSelect] = "multiselect",
        [FieldType.Relation] = "relation",
        [FieldType.Attachment] = "attachment",
        [FieldType.AutoNumber] = "autonumber"
    };

    private static readonly Dictionary<string, FieldType> ByWireName =
        WireNames.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyCollection<FieldType> All { get; } = WireNames.Keys.ToArray();

    public static string ToWireName(this FieldType type) =>
        WireNames.TryGetValue(type, out var name)
            ? name
            : throw new ArgumentOutOfRangeException(nameof(type), type, "未知字段类型。");

    public static bool TryParse(string? value, out FieldType type)
    {
        type = default;
        return !string.IsNullOrWhiteSpace(value) && ByWireName.TryGetValue(value, out type);
    }

    public static FieldType Parse(string? value) =>
        TryParse(value, out var type)
            ? type
            : throw new ArgumentException($"未知字段类型：{value}", nameof(value));
}
