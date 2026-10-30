namespace ZX.Platform.LowCode.Domain;

/// <summary>单选 / 多选的选项。</summary>
public sealed class FieldOption
{
    public string Value { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public string? LabelEn { get; init; }
}

/// <summary>
/// 字段定义。这是平台最底层的模型：物理列、前端控件、校验、算子全部由它推导。
/// 类型一旦发布不可修改，见 field-types.md §5。
/// </summary>
public sealed class FieldDefinition
{
    public const int DefaultTextLength = 200;
    public const int MaxTextLength = 2000;
    public const int MaxLongTextLength = 32000;
    public const int DefaultPrecision = 18;
    public const int MaxPrecision = 18;
    public const int MaxOptionValueLength = 64;
    public const int MaxAutoNumberPrefixLength = 8;

    /// <summary>JSON 里的字段名，小写蛇形，同时是物理列名（大写）。</summary>
    public string Key { get; init; } = string.Empty;

    public FieldType Type { get; init; }

    public string Name { get; init; } = string.Empty;

    public string? NameEn { get; init; }

    public bool Required { get; init; }

    public bool Unique { get; init; }

    public bool Indexed { get; init; }

    /// <summary>仅 text / longtext 有意义。</summary>
    public int? MaxLength { get; init; }

    /// <summary>仅 number 有意义，amount / percent 的精度是固定的。</summary>
    public int? Precision { get; init; }

    /// <summary>字符串形式的默认值，按类型解析，见 field-types.md §6。</summary>
    public string? DefaultValue { get; init; }

    /// <summary>relation 字段的关联实体 code。</summary>
    public string? RefEntity { get; init; }

    /// <summary>relation 字段下拉里显示的字段 key，默认 code。</summary>
    public string? RefDisplayField { get; init; }

    /// <summary>autonumber 的编码前缀。</summary>
    public string? Prefix { get; init; }

    public int Sort { get; init; }

    /// <summary>停用字段：物理列保留，元数据不再渲染，见 field-types.md §5。</summary>
    public bool Disabled { get; init; }

    public IReadOnlyList<FieldOption> Options { get; init; } = [];

    public int EffectiveMaxLength => MaxLength ?? Type switch
    {
        FieldType.Text => DefaultTextLength,
        FieldType.LongText => MaxLongTextLength,
        _ => 0
    };

    public int EffectivePrecision => Precision ?? DefaultPrecision;

    /// <summary>物理列名。</summary>
    public string ColumnName => Key.ToUpperInvariant();

    public bool HasDefault => !string.IsNullOrEmpty(DefaultValue);

    public FieldOption? FindOption(string value) =>
        Options.FirstOrDefault(option => string.Equals(option.Value, value, StringComparison.Ordinal));
}
