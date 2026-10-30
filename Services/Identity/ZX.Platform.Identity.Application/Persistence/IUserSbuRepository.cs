using ZX.Platform.Identity.Domain.Entities;

namespace ZX.Platform.Identity.Application.Persistence
{
    public interface IUserSbuRepository
    {
        /// <summary>
        /// 根据用户标识查询用户关联的SBU列表。
        /// </summary>
        Task<List<UserSbu>> GetByUserIdAsync(
            Guid userId,
            CancellationToken cancellationToken);

        /// <summary>
        /// 查询用户关联的指定 SBU；不存在、已删除或停用时返回 null。
        /// </summary>
        Task<Sbu?> GetEnabledSbuForUserAsync(
            Guid userId,
            Guid sbuId,
            CancellationToken cancellationToken);

        /// <summary>
        /// 一次查询多个用户的SBU关联，用于分页列表展示。
        /// </summary>
        Task<List<UserSbu>> GetByUserIdsAsync(
            IReadOnlyCollection<Guid> userIds,
            CancellationToken cancellationToken);

        /// <summary>
        /// 登记一个待新增的用户SBU关联，调用后不会立即写入数据库。
        /// </summary>
        void Add(UserSbu userSbu);

        /// <summary>
        /// 登记一个待删除的用户SBU关联，调用后不会立即写入数据库。
        /// </summary>
        void Remove(UserSbu userSbu);
    }
}
