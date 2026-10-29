using System.ComponentModel.DataAnnotations;

namespace ZX.Platform.Identity.Application.Users.Dtos
{
    public class UpdateUserSbuDto
    {
        [Required(ErrorMessage = "Validation.UserIdRequired")]
        public Guid UserId { get; set; }

        [Required(ErrorMessage = "Validation.SbusRequired")]
        public List<Guid> SbuIds { get; set; } = new();
    }
}
