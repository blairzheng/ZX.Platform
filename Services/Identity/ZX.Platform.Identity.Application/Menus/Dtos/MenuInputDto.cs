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
    [Range(0, 10000, ErrorMessage = "Validation.MenuSort")]
    public int Sort { get; set; }
    public bool KeepAlive { get; set; } = true;
    public bool ShowInMenu { get; set; } = true;
    public bool ShowInTags { get; set; } = true;
    public bool Affix { get; set; }
}

public sealed class CreateMenuDto : MenuInputDto
{
    [Required(ErrorMessage = "Validation.Required")]
    [StringLength(100, ErrorMessage = "Validation.StringLength")]
    [RegularExpression("[a-zA-Z][a-zA-Z0-9_-]*", ErrorMessage = "Validation.MenuCode")]
    public string Code { get; set; } = string.Empty;
}

public sealed class UpdateMenuDto : MenuInputDto
{
    public Guid Id { get; set; }
}
