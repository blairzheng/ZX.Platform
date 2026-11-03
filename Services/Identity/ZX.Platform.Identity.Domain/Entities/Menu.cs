using ZX.Platform.Identity.Domain.Auditing;
using ZX.Platform.Identity.Domain.Exceptions;

namespace ZX.Platform.Identity.Domain.Entities;

public sealed class Menu : SoftDeletableEntity
{
    public const string Directory = "directory";
    public const string Page = "page";
    public const string Action = "action";
    private string? _titleEn;
    private string? _icon;
    private string? _path;
    private string? _component;
    private string? _permissionCode;
    private string? _launcherTone;
    private string? _launcherDescription;
    private string? _launcherDescriptionEn;
    private Menu() { }
    public Guid Id { get; private set; }
    public Guid? ParentId { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string Kind { get; private set; } = string.Empty;
    public string Title { get; private set; } = string.Empty;
    // 数据库允许可选文本为 NULL，领域和接口继续使用空字符串。
    public string TitleEn { get => _titleEn ?? string.Empty; private set => _titleEn = value; }
    public string Icon { get => _icon ?? string.Empty; private set => _icon = value; }
    public string Path { get => _path ?? string.Empty; private set => _path = value; }
    public string Component { get => _component ?? string.Empty; private set => _component = value; }
    public string PermissionCode { get => _permissionCode ?? string.Empty; private set => _permissionCode = value; }
    public string LauncherTone { get => _launcherTone ?? "primary"; private set => _launcherTone = value; }
    public string LauncherDescription { get => _launcherDescription ?? string.Empty; private set => _launcherDescription = value; }
    public string LauncherDescriptionEn { get => _launcherDescriptionEn ?? string.Empty; private set => _launcherDescriptionEn = value; }
    public int Sort { get; private set; }
    public bool KeepAlive { get; private set; }
    public bool ShowInMenu { get; private set; } = true;
    public bool ShowInTags { get; private set; } = true;
    public bool Affix { get; private set; }
    public bool IsEnabled { get; private set; } = true;
    public bool IsBuiltIn { get; private set; }

    public static Menu Create(string code, string kind, Guid? parentId, string title, string titleEn,
        string icon, string path, string component, string permissionCode, int sort, bool keepAlive, bool builtIn = false,
        bool showInMenu = true, bool showInTags = true, bool affix = false, string launcherTone = "primary",
        string launcherDescription = "", string launcherDescriptionEn = "") => new()
    {
        Id = Guid.NewGuid(), Code = code.Trim(), Kind = kind, ParentId = parentId,
        Title = title.Trim(), TitleEn = titleEn.Trim(), Icon = icon.Trim(), Path = path.Trim(),
        Component = component.Trim(), PermissionCode = permissionCode.Trim(), Sort = sort,
        LauncherTone = launcherTone.Trim(), LauncherDescription = launcherDescription.Trim(),
        LauncherDescriptionEn = launcherDescriptionEn.Trim(),
        KeepAlive = keepAlive, ShowInMenu = showInMenu, ShowInTags = showInTags, Affix = affix,
        IsBuiltIn = builtIn
    };

    public void Update(string kind, Guid? parentId, string title, string titleEn, string icon,
        string path, string component, string permissionCode, int sort, bool keepAlive,
        bool showInMenu, bool showInTags, bool affix, string launcherTone = "primary",
        string launcherDescription = "", string launcherDescriptionEn = "")
    {
        // 内置菜单只允许调整父级；编码、展示内容、路由、组件和授权关联保持稳定。
        if (IsBuiltIn && (Kind != kind || Title != title.Trim() || TitleEn != titleEn.Trim() ||
            Icon != icon.Trim() || Path != path || Component != component ||
            PermissionCode != permissionCode || LauncherTone != launcherTone.Trim() ||
            LauncherDescription != launcherDescription.Trim() ||
            LauncherDescriptionEn != launcherDescriptionEn.Trim() || Sort != sort || KeepAlive != keepAlive ||
            ShowInMenu != showInMenu || ShowInTags != showInTags || Affix != affix))
            throw new DomainException("Access.BuiltInMenuStructureReadOnly");
        Kind = kind; ParentId = parentId; Title = title.Trim(); TitleEn = titleEn.Trim();
        Icon = icon.Trim(); Path = path.Trim(); Component = component.Trim();
        PermissionCode = permissionCode.Trim(); LauncherTone = launcherTone.Trim();
        LauncherDescription = launcherDescription.Trim(); LauncherDescriptionEn = launcherDescriptionEn.Trim();
        Sort = sort; KeepAlive = keepAlive;
        ShowInMenu = showInMenu; ShowInTags = showInTags; Affix = affix;
    }

    public void SetEnabled(bool enabled)
    {
        EnsureRemovable();
        IsEnabled = enabled;
    }
    public void EnsureRemovable()
    {
        if (IsBuiltIn) throw new DomainException("Access.BuiltInMenuStructureReadOnly");
    }
    protected override void OnDeleted() { EnsureRemovable(); IsEnabled = false; }
    protected override void OnRestored() { EnsureRemovable(); IsEnabled = true; }
}
