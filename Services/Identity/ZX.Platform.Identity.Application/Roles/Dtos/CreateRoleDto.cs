using System.ComponentModel.DataAnnotations;

namespace ZX.Platform.Identity.Application.Roles.Dtos;

public sealed class CreateRoleDto
{
    [Required(ErrorMessage = "Validation.Required")]
    [StringLength(50, ErrorMessage = "Validation.StringLength")]
    [RegularExpression(@"[a-zA-Z][a-zA-Z0-9_]*", ErrorMessage = "Validation.RoleCode")]
    public string Code { get; set; } = string.Empty;

    [Required(ErrorMessage = "Validation.Required")]
    [StringLength(200, ErrorMessage = "Validation.StringLength")]
    public string Name { get; set; } = string.Empty;

    [StringLength(500, ErrorMessage = "Validation.StringLength")]
    public string? Description { get; set; }
}
