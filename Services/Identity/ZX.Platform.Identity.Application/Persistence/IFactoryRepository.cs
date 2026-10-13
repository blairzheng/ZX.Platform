using ZX.Platform.Identity.Domain.Entities;

namespace ZX.Platform.Identity.Application.Persistence
{
    public interface IFactoryRepository
    {
        /// <summary>
        /// 根据厂区标识查询未删除厂区。
        /// </summary>
        Task<Factory?> GetByIdAsync(
            Guid factoryId,
            CancellationToken cancellationToken);

        /// <summary>
        /// 根据厂区标识查询厂区，包含已经逻辑删除的厂区。
        /// </summary>
        Task<Factory?> GetByIdIncludeDeletedAsync(
            Guid factoryId,
            CancellationToken cancellationToken);

        /// <summary>
        /// 根据厂区编码查询厂区，包含已经逻辑删除的厂区。
        /// 用于厂区编码唯一性检查和提供友好提示。
        /// </summary>
        Task<Factory?> GetByCodeAsync(
            string code,
            CancellationToken cancellationToken);

        /// <summary>
        /// 登记一个待新增厂区，调用后不会立即写入数据库。
        /// </summary>
        void Add(Factory factory);

        /// <summary>
        /// 获取所有未删除厂区列表。厂区数据量小，不提供分页。
        /// </summary>
        Task<List<Factory>> GetAllAsync(CancellationToken cancellationToken);

        /// <summary>
        /// 获取所有启用的厂区列表。
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        Task<List<Factory>> GetAllEnabledFactoriesAsync(CancellationToken cancellationToken);

        /// <summary>
        /// 过滤前台传过来存在的厂区数据
        /// </summary>
        /// <param name="factoryIds"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        Task<List<Factory>> GetEnabledFactoriesByIdsAsync(List<Guid> factoryIds, CancellationToken cancellationToken);
    }
}
