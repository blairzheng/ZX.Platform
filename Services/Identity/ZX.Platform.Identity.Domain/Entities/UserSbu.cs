using ZX.Platform.Identity.Domain.Auditing;

namespace ZX.Platform.Identity.Domain.Entities
{
    public sealed class UserSbu : AuditableEntity
    {
        private UserSbu()
        {
        }

        private UserSbu(
            Guid userId,
            Guid sbuId,
            bool isDefault)
        {
            UserId = userId;
            SbuId = sbuId;
            IsDefault = isDefault;
        }

        /// <summary>
        /// 用户唯一标识。
        /// </summary>
        public Guid UserId { get; private set; }

        /// <summary>
        /// SBU唯一标识。
        /// </summary>
        public Guid SbuId { get; private set; }

        /// <summary>
        /// 是否为用户的默认SBU。
        /// </summary>
        public bool IsDefault { get; private set; }

        /// <summary>
        /// 创建用户与SBU的关联关系。
        /// 不负责写入数据库。
        /// </summary>
        public static UserSbu Create(
            Guid userId,
            Guid sbuId,
            bool isDefault)
        {
            if (userId == Guid.Empty)
                throw new ArgumentException("用户标识不能为空。", nameof(userId));

            if (sbuId == Guid.Empty)
                throw new ArgumentException("SBU标识不能为空。", nameof(sbuId));

            return new UserSbu(
                userId,
                sbuId,
                isDefault);
        }

        /// <summary>
        /// 将当前关联设置为默认SBU。
        /// 一个用户只能有一个默认SBU，
        /// 其他关联需要由 Application Service 一起取消默认状态。
        /// </summary>
        public void SetAsDefault()
        {
            IsDefault = true;
        }

        /// <summary>
        /// 取消当前关联的默认SBU状态。
        /// </summary>
        public void CancelDefault()
        {
            IsDefault = false;
        }
    }
}
