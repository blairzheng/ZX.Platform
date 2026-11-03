using System.ComponentModel.DataAnnotations;

namespace ZX.Platform.Identity.Application.Sbus.Dtos
{
    public class UpdateSbuNameDto
    {
        public Guid Id { get; set; }

        [Required(ErrorMessage = "Validation.SbuNameRequired")]
        [MaxLength(200, ErrorMessage = "Validation.SbuNameMaxLength")]
        public string Name { get; set; } = string.Empty;
    }
}
