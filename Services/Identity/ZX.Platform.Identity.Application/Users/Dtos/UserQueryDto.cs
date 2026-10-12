using ZX.Platform.Identity.Application.Common;

namespace ZX.Platform.Identity.Application.Users.Dtos
{
    public class UserQueryDto : PageQueryDto
    {
        public string? Keyword { get; set; }

        public bool? IsEnabled { get; set; }

        public Guid? FactoryId { get; set; }
    }
}
