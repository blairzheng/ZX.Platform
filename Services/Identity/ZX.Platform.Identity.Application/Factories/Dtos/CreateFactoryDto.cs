using System.ComponentModel.DataAnnotations;

namespace ZX.Platform.Identity.Application.Factories.Dtos
{
    public class CreateFactoryDto
    {
        [Required(ErrorMessage = "Validation.FactoryCodeRequired")]
        [MaxLength(50, ErrorMessage = "Validation.FactoryCodeMaxLength")]
        public string Code { get; set; } = string.Empty;

        [Required(ErrorMessage = "Validation.FactoryNameRequired")]
        [MaxLength(200, ErrorMessage = "Validation.FactoryNameMaxLength")]
        public string Name { get; set; } = string.Empty;
    }
}
