using ZX.Platform.Identity.Application.Common;

namespace ZX.Platform.Identity.Application.Users.Dtos
{
    public class UserQueryDto : PageQueryDto
    {
        public string? Keyword { get; set; }

        public bool? IsEnabled { get; set; }

        public Guid? SbuId { get; set; }

        // 创建时间下界（包含），使用带偏移量的 ISO 8601 时间。
        public DateTimeOffset? CreatedFromUtc { get; set; }

        // 创建时间上界（不包含）。
        public DateTimeOffset? CreatedToUtc { get; set; }
    }
}
