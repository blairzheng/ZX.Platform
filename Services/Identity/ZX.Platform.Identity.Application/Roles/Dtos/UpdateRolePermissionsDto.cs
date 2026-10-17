using System.ComponentModel.DataAnnotations;

namespace ZX.Platform.Identity.Application.Roles.Dtos;

public sealed class UpdateRolePermissionsDto
{
    public Guid RoleId { get; set; }
    [Required(ErrorMessage = "Validation.Required")]
    [MaxLength(500, ErrorMessage = "Validation.MaxLength")]
    public List<string> PermissionCodes { get; set; } = [];
}
