namespace ZX.Platform.Identity.Application.Factories.Dtos
{
    public class FactoryDetailDto
    {
        /// <summary>
        /// 厂区唯一标识
        /// </summary>
        public Guid Id { get; set; }

        /// <summary>
        /// 厂区编码，例如 VNK、VNY、F
        /// </summary>
        public string Code { get; set; } = string.Empty;

        /// <summary>
        /// 厂区显示名称
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// 厂区是否启用
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
