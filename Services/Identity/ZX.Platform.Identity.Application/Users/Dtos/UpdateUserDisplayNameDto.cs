using System.ComponentModel.DataAnnotations;

namespace ZX.Platform.Identity.Application.Users.Dtos
{
    public class UpdateUserDisplayNameDto
    {
        public Guid Id { get; set; }

        [Required(ErrorMessage = "Validation.DisplayNameRequired")]
        [MaxLength(100, ErrorMessage = "Validation.DisplayNameMaxLength")]
        public string DisplayName { get; set; } = string.Empty;
    }
}
