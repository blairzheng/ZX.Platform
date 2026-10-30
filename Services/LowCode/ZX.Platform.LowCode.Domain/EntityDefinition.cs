namespace ZX.Platform.LowCode.Domain;

/// <summary>
/// 实体定义 = 一张物理表 + 一组字段 + 若干视图。
/// 这是元数据的核心聚合，字段与视图整体加载，不拆表。
/// </summary>
public sealed class EntityDefinition
{
    public const int CurrentSchemaVersion = 1;
    public const int MaxFieldCount = 200;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public string Code { get; init; } = string.Empty;

    public string AppCode { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string? NameEn { get; init; }

    public ScopeMode ScopeMode { get; init; } = ScopeMode.None;

    /// <summary>scopeMode = column 时使用，目前只允许 SBU_ID。</summary>
    public string? ScopeField { get; init; }

    public bool AuditEnabled { get; init; } = true;

    public IReadOnlyList<FieldDefinition> Fields { get; init; } = [];

    public IReadOnlyList<ViewDefinition> Views { get; init; } = [];

    public string TableName => Naming.TableName(AppCode, Code);

    public bool HasSbuScope => ScopeMode == ScopeMode.Column;

    public string EffectiveScopeField =>
        string.IsNullOrWhiteSpace(ScopeField) ? ReservedColumns.SbuId : ScopeField;

    public FieldDefinition? FindField(string? key) =>
        string.IsNullOrWhiteSpace(key)
            ? null
            : Fields.FirstOrDefault(field => string.Equals(field.Key, key, StringComparison.OrdinalIgnoreCase));

    public FieldDefinition? FindFieldByColumn(string column) =>
        Fields.FirstOrDefault(field => string.Equals(field.ColumnName, column, StringComparison.OrdinalIgnoreCase));

    /// <summary>参与读写的字段（排除已停用）。</summary>
    public IEnumerable<FieldDefinition> ActiveFields => Fields.Where(item => !item.Disabled);

    public string PermissionPrefix => Naming.PermissionCode(AppCode, Code, string.Empty).TrimEnd('.');
}
