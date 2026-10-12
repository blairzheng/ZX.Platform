using ZX.Platform.Identity.Domain.Entities;

namespace ZX.Platform.Identity.Application.Persistence
{
    public interface IUserFactoryRepository
    {
        /// <summary>
        /// 根据用户标识查询用户关联的厂区列表。
        /// </summary>
        Task<List<UserFactory>> GetByUserIdAsync(
            Guid userId,
            CancellationToken cancellationToken);

        /// <summary>
        /// 一次查询多个用户的厂区关联，用于分页列表展示。
        /// </summary>
        Task<List<UserFactory>> GetByUserIdsAsync(
            IReadOnlyCollection<Guid> userIds,
            CancellationToken cancellationToken);

        /// <summary>
        /// 登记一个待新增的用户厂区关联，调用后不会立即写入数据库。
        /// </summary>
        void Add(UserFactory userFactory);

        /// <summary>
        /// 登记一个待删除的用户厂区关联，调用后不会立即写入数据库。
        /// </summary>
        void Remove(UserFactory userFactory);
    }
}
