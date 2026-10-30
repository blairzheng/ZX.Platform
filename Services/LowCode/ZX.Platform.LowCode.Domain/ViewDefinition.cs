namespace ZX.Platform.LowCode.Domain;

/// <summary>列表视图里的一列。</summary>
public sealed class ViewColumn
{
    public string Field { get; init; } = string.Empty;
    public int? Width { get; init; }
    public bool Visible { get; init; } = true;
    public int Sort { get; init; }
}

/// <summary>视图默认排序。</summary>
public sealed class ViewSort
{
    public string Field { get; init; } = string.Empty;
    public bool Descending { get; init; }
}

/// <summary>
/// 实体的一个列表视图。视图只影响展示，不影响权限与数据范围。
/// </summary>
public sealed class ViewDefinition
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 200;

    public string Code { get; init; } = "default";

    public string Name { get; init; } = string.Empty;

    public string? NameEn { get; init; }

    public int PageSize { get; init; } = DefaultPageSize;

    public IReadOnlyList<ViewColumn> Columns { get; init; } = [];

    public IReadOnlyList<ViewSort> DefaultSort { get; init; } = [];

    public IReadOnlyList<string> FilterFields { get; init; } = [];

    public int EffectivePageSize => PageSize <= 0 ? DefaultPageSize : Math.Min(PageSize, MaxPageSize);
}
