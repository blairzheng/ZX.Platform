namespace ZX.Platform.Identity.Domain.Auditing
{
    public abstract class AuditableEntity
    {
        /// <summary>
        /// 创建时间，统一使用 UTC 时间。
        /// </summary>
        public DateTimeOffset CreatedAtUtc { get; protected set; }

        /// <summary>
        /// 创建人标识；系统自动创建的数据可以为空。
        /// </summary>
        public Guid? CreatedByUserId { get; protected set; }

        /// <summary>
        /// 最后修改时间，统一使用 UTC 时间。
        /// </summary>
        public DateTimeOffset? UpdatedAtUtc { get; protected set; }

        /// <summary>
        /// 最后修改人标识。
        /// </summary>
        public Guid? UpdatedByUserId { get; protected set; }
    }
}
