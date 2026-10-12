using System.ComponentModel.DataAnnotations;

namespace ZX.Platform.Identity.Application.Users.Dtos
{
    public class UpdateUserFactoryDto
    {
        [Required(ErrorMessage = "Validation.UserIdRequired")]
        public Guid UserId { get; set; }

        [Required(ErrorMessage = "Validation.FactoriesRequired")]
        [MinLength(1, ErrorMessage = "Validation.FactoriesMinLength")]
        public List<Guid> FactoryIds { get; set; } = new();
    }
}
