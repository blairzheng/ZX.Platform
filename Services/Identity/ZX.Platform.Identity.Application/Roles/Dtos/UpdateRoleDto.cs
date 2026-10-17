using System.ComponentModel.DataAnnotations;

namespace ZX.Platform.Identity.Application.Roles.Dtos;

public sealed class UpdateRoleDto
{
    public Guid Id { get; set; }
    [Required(ErrorMessage = "Validation.Required")]
    [StringLength(200, ErrorMessage = "Validation.StringLength")]
    public string Name { get; set; } = string.Empty;
    [StringLength(500, ErrorMessage = "Validation.StringLength")]
    public string? Description { get; set; }
}
