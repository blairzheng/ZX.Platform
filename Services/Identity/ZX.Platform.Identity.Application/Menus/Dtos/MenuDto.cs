namespace ZX.Platform.Identity.Application.Menus.Dtos;

public sealed class MenuDto
{
    public Guid Id { get; init; }
    public Guid? ParentId { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Kind { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string TitleEn { get; init; } = string.Empty;
    public string Icon { get; init; } = string.Empty;
    public string Path { get; init; } = string.Empty;
    public string Component { get; init; } = string.Empty;
    public string PermissionCode { get; init; } = string.Empty;
    public int Sort { get; init; }
    public bool KeepAlive { get; init; }
    public bool ShowInMenu { get; init; } = true;
    public bool ShowInTags { get; init; } = true;
    public bool Affix { get; init; }
    public bool IsEnabled { get; init; } = true;
    public bool IsBuiltIn { get; init; }
    public List<MenuDto> Children { get; init; } = [];
}
