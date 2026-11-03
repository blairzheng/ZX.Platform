using ZX.Platform.Identity.Application.Users.Dtos;
using ZX.Platform.Identity.Domain.Entities;

namespace ZX.Platform.Identity.Application.Persistence
{
    public interface IUserRepository
    {
        /// <summary>
        /// 根据用户标识查询未删除用户。
        /// </summary>
        Task<User?> GetByIdAsync(
            Guid userId,
            CancellationToken cancellationToken);
        /// <summary>
        /// 根据用户标识查询用户，包含已经逻辑删除的用户。
        /// </summary>
        /// <param name="userId"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        Task<User?> GetByIdIncludeDeletedAsync(
            Guid userId,
            CancellationToken cancellationToken);

        /// <summary>
        /// 根据标准化账号查询用户，包含已经逻辑删除的用户。
        /// 用于账号唯一性检查和提供友好提示。
        /// </summary>
        Task<User?> GetByNormalizedAccountAsync(
            string normalizedAccount,
            CancellationToken cancellationToken);

        /// <summary>
        /// 按工号查询用户，包含已经逻辑删除的用户。
        /// 已删除或已拒绝的工号仍然占位，不能因为查不到未删除记录就重新建档。
        /// </summary>
        Task<User?> GetByEmployeeNoAsync(
            string employeeNo,
            CancellationToken cancellationToken);

        /// <summary>
        /// 按目录来源和目录唯一标识查询用户，包含已经逻辑删除的用户。
        /// </summary>
        Task<User?> GetByLdapIdentityAsync(
            string provider,
            string subject,
            CancellationToken cancellationToken);

        /// <summary>
        /// 登记一个待新增用户，调用后不会立即写入数据库。
        /// </summary>
        void Add(User user);

        /// <summary>
        /// 取消对实体的跟踪，用于唯一约束冲突后重新读取真实记录。
        /// </summary>
        void Detach(User user);

        /// <summary>
        /// 获取用户分页列表
        /// </summary>
        /// <param name="userQuery"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        Task<(List<User>, int count)> GetPageListAsync(UserQueryDto userQuery, CancellationToken cancellationToken);

        Task<List<User>> GetUsersAsync(List<Guid> userIds, CancellationToken cancellationToken);
    }
}
