using System.ComponentModel.DataAnnotations;

namespace ZX.Platform.Identity.Application.Factories.Dtos
{
    public class UpdateFactoryNameDto
    {
        public Guid Id { get; set; }

        [Required(ErrorMessage = "Validation.FactoryNameRequired")]
        [MaxLength(200, ErrorMessage = "Validation.FactoryNameMaxLength")]
        public string Name { get; set; } = string.Empty;
    }
}
