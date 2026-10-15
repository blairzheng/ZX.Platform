using System.ComponentModel.DataAnnotations;
using ZX.Platform.Identity.Application.Security;

namespace ZX.Platform.Identity.Application.Authentication.Dtos;

public sealed class ChangePasswordDto
{
    [Required(ErrorMessage = "Validation.CurrentPasswordRequired")]
    [PasswordBytes]
    public string CurrentPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "Validation.NewPasswordRequired")]
    [MinLength(8, ErrorMessage = "Validation.NewPasswordMinLength")]
    [PasswordBytes]
    public string NewPassword { get; set; } = string.Empty;
}
