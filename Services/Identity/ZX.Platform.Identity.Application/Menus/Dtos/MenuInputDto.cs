using System.ComponentModel.DataAnnotations;

namespace ZX.Platform.Identity.Application.Menus.Dtos;

public class MenuInputDto
{
    public Guid? ParentId { get; set; }
    [Required(ErrorMessage = "Validation.Required")]
    [RegularExpression("directory|page|action", ErrorMessage = "Validation.MenuKind")]
    public string Kind { get; set; } = "page";
    [Required(ErrorMessage = "Validation.Required")]
    [StringLength(200, ErrorMessage = "Validation.StringLength")]
    public string Title { get; set; } = string.Empty;
    [StringLength(200, ErrorMessage = "Validation.StringLength")]
    public string TitleEn { get; set; } = string.Empty;
    [StringLength(100, ErrorMessage = "Validation.StringLength")]
    public string Icon { get; set; } = string.Empty;
    [StringLength(200, ErrorMessage = "Validation.StringLength")]
    public string Path { get; set; } = string.Empty;
    [StringLength(100, ErrorMessage = "Validation.StringLength")]
    public string Component { get; set; } = string.Empty;
    [StringLength(100, ErrorMessage = "Validation.StringLength")]
    public string PermissionCode { get; set; } = string.Empty;
    [RegularExpression("primary|warning|success|violet", ErrorMessage = "Validation.MenuLauncherTone")]
    public string LauncherTone { get; set; } = "primary";
    [StringLength(500, ErrorMessage = "Validation.StringLength")]
    public string LauncherDescription { get; set; } = string.Empty;
    [StringLength(500, ErrorMessage = "Validation.StringLength")]
    public string LauncherDescriptionEn { get; set; } = string.Empty;
    [Range(0, 10000, ErrorMessage = "Validation.MenuSort")]
    public int Sort { get; set; }
    public bool KeepAlive { get; set; } = true;
    public bool ShowInMenu { get; set; } = true;
    public bool ShowInTags { get; set; } = true;
    public bool Affix { get; set; }
}

public sealed class CreateMenuDto : MenuInputDto
{
    // 保留属性以兼容旧客户端；新菜单编码由服务端生成，客户端传入值不会被使用。
    public string? Code { get; set; }
}

public sealed class UpdateMenuDto : MenuInputDto
{
    public Guid Id { get; set; }
}
