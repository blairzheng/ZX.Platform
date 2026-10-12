using System.ComponentModel.DataAnnotations;

namespace ZX.Platform.Identity.Api.Options
{
    /// <summary>
    /// 普通业务接口的频率限制；修改配置后重启服务。
    /// </summary>
    public sealed class ApiRateLimitingOptions
    {
        [Range(1, int.MaxValue)]
        public int PermitLimit { get; set; } = 5;

        [Range(1, int.MaxValue)]
        public int WindowSeconds { get; set; } = 3;

        [Range(1, int.MaxValue)]
        public int SegmentsPerWindow { get; set; } = 3;
    }
}
