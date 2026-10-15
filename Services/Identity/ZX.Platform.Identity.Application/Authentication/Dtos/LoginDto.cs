using System.ComponentModel.DataAnnotations;
using ZX.Platform.Identity.Application.Security;

namespace ZX.Platform.Identity.Application.Authentication.Dtos;

public sealed class LoginDto
{
    [Required(ErrorMessage = "Validation.AccountRequired")]
    [MaxLength(8, ErrorMessage = "Validation.AccountMaxLength")]
    public string Account { get; set; } = string.Empty;

    [Required(ErrorMessage = "Validation.PasswordRequired")]
    [PasswordBytes]
    public string Password { get; set; } = string.Empty;

    public bool ReplaceExistingSession { get; set; }
}
