using System.ComponentModel.DataAnnotations;

namespace ZX.Platform.Identity.Infrastructure.Security;

public sealed class RedisSessionOptions
{
    [Required]
    [RegularExpression("^[A-Za-z0-9:_-]+$")]
    public string KeyPrefix { get; set; } = "zx:identity:auth:";
}
