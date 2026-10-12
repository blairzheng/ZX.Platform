using System.ComponentModel.DataAnnotations;

namespace ZX.Platform.Identity.Application.Users.Dtos
{
    public class UpdateUserPasswordDto
    {
        public Guid Id { get; set; }

        [Required(ErrorMessage = "Validation.NewPasswordRequired")]
        [MinLength(8, ErrorMessage = "Validation.NewPasswordMinLength")]
        [MaxLength(72, ErrorMessage = "Validation.NewPasswordMaxLength")]
        public string NewPassword { get; set; } = string.Empty;
    }
}
