namespace ZX.Platform.Identity.Application.Sbus.Dtos
{
    public class SbuDetailDto
    {
        /// <summary>
        /// SBU唯一标识
        /// </summary>
        public Guid Id { get; set; }

        /// <summary>
        /// SBU编码，例如 VNK、VNY、F
        /// </summary>
        public string Code { get; set; } = string.Empty;

        /// <summary>
        /// SBU显示名称
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// SBU是否启用
        /// </summary>
        public bool IsEnabled { get; set; }

        /// <summary>
        /// 创建时间，统一使用 UTC 时间。
        /// </summary>
        public DateTimeOffset CreatedAtUtc { get; set; }

        /// <summary>
        /// 最后修改时间，统一使用 UTC 时间。
        /// </summary>
        public DateTimeOffset? UpdatedAtUtc { get; set; }
    }
}
