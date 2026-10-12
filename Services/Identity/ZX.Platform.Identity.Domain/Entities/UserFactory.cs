using ZX.Platform.Identity.Domain.Auditing;

namespace ZX.Platform.Identity.Domain.Entities
{
    public sealed class UserFactory : AuditableEntity
    {
        private UserFactory()
        {
        }

        private UserFactory(
            Guid userId,
            Guid factoryId,
            bool isDefault)
        {
            UserId = userId;
            FactoryId = factoryId;
            IsDefault = isDefault;
        }

        /// <summary>
        /// 用户唯一标识。
        /// </summary>
        public Guid UserId { get; private set; }

        /// <summary>
        /// 厂区唯一标识。
        /// </summary>
        public Guid FactoryId { get; private set; }

        /// <summary>
        /// 是否为用户的默认厂区。
        /// </summary>
        public bool IsDefault { get; private set; }

        /// <summary>
        /// 创建用户与厂区的关联关系。
        /// 不负责写入数据库。
        /// </summary>
        public static UserFactory Create(
            Guid userId,
            Guid factoryId,
            bool isDefault)
        {
            if (userId == Guid.Empty)
                throw new ArgumentException("用户标识不能为空。", nameof(userId));

            if (factoryId == Guid.Empty)
                throw new ArgumentException("厂区标识不能为空。", nameof(factoryId));

            return new UserFactory(
                userId,
                factoryId,
                isDefault);
        }

        /// <summary>
        /// 将当前关联设置为默认厂区。
        /// 一个用户只能有一个默认厂区，
        /// 其他关联需要由 Application Service 一起取消默认状态。
        /// </summary>
        public void SetAsDefault()
        {
            IsDefault = true;
        }

        /// <summary>
        /// 取消当前关联的默认厂区状态。
        /// </summary>
        public void CancelDefault()
        {
            IsDefault = false;
        }
    }
}
