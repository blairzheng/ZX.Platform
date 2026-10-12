using ZX.Platform.Identity.Application.Common;

namespace ZX.Platform.Identity.Application.Factories.Dtos
{
    public class FactoryQueryDto : PageQueryDto
    {
        public string? Keyword { get; set; }
    }
}
