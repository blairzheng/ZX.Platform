using System.ComponentModel.DataAnnotations;

namespace ZX.Platform.Identity.Application.Roles.Dtos;

public sealed class UpdateUserRolesDto
{
    public Guid UserId { get; set; }
    [Required(ErrorMessage = "Validation.Required")]
    [MaxLength(200, ErrorMessage = "Validation.MaxLength")]
    public List<Guid> RoleIds { get; set; } = [];
}
