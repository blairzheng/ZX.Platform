using System.ComponentModel.DataAnnotations;

using ZX.Platform.Identity.Application.Security;

namespace ZX.Platform.Identity.Application.Users.Dtos
{
    public class CreateUserDto
    {
        [Required(ErrorMessage = "Validation.AccountRequired")]
        [MaxLength(8, ErrorMessage = "Validation.AccountMaxLength")]
        public string Account { get; set; } = string.Empty;

        [Required(ErrorMessage = "Validation.DisplayNameRequired")]
        [MaxLength(100, ErrorMessage = "Validation.DisplayNameMaxLength")]
        public string DisplayName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Validation.PasswordRequired")]
        [MinLength(8, ErrorMessage = "Validation.PasswordMinLength")]
        [PasswordBytes]
        public string Password { get; set; } = string.Empty;

        [Required(ErrorMessage = "Validation.SbusRequired")]
        public List<Guid> SbuIds { get; set; } = new();
    }
}
