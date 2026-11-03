using System.ComponentModel.DataAnnotations;

namespace ZX.Platform.Identity.Application.Sbus.Dtos
{
    public class CreateSbuDto
    {
        [Required(ErrorMessage = "Validation.SbuCodeRequired")]
        [MaxLength(50, ErrorMessage = "Validation.SbuCodeMaxLength")]
        public string Code { get; set; } = string.Empty;

        [Required(ErrorMessage = "Validation.SbuNameRequired")]
        [MaxLength(200, ErrorMessage = "Validation.SbuNameMaxLength")]
        public string Name { get; set; } = string.Empty;
    }
}
