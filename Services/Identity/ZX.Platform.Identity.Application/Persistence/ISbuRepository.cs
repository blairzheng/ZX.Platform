using ZX.Platform.Identity.Domain.Entities;

namespace ZX.Platform.Identity.Application.Persistence
{
    public interface ISbuRepository
    {
        /// <summary>
        /// 根据SBU标识查询未删除SBU。
        /// </summary>
        Task<Sbu?> GetByIdAsync(
            Guid sbuId,
            CancellationToken cancellationToken);

        /// <summary>
        /// 根据SBU标识查询SBU，包含已经逻辑删除的SBU。
        /// </summary>
        Task<Sbu?> GetByIdIncludeDeletedAsync(
            Guid sbuId,
            CancellationToken cancellationToken);

        /// <summary>
        /// 根据SBU编码查询SBU，包含已经逻辑删除的SBU。
        /// 用于SBU编码唯一性检查和提供友好提示。
        /// </summary>
        Task<Sbu?> GetByCodeAsync(
            string code,
            CancellationToken cancellationToken);

        /// <summary>
        /// 登记一个待新增SBU，调用后不会立即写入数据库。
        /// </summary>
        void Add(Sbu sbu);

        /// <summary>
        /// 获取所有未删除SBU列表。SBU数据量小，不提供分页。
        /// </summary>
        Task<List<Sbu>> GetAllAsync(CancellationToken cancellationToken);

        /// <summary>
        /// 获取所有启用的SBU列表。
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        Task<List<Sbu>> GetAllEnabledSbusAsync(CancellationToken cancellationToken);

        /// <summary>
        /// 过滤前台传过来存在的SBU数据
        /// </summary>
        /// <param name="sbuIds"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        Task<List<Sbu>> GetEnabledSbusByIdsAsync(List<Guid> sbuIds, CancellationToken cancellationToken);
    }
}
