using ZX.Platform.Identity.Domain.Entities;

namespace ZX.Platform.Identity.Application.Personnel;

public interface IOaPersonnelUserRepository
{
    Task<List<User>> GetAllAsync(string businessUnit, CancellationToken cancellationToken);

    /// <summary>跟踪所有用户，包括禁用及软删除记录，避免重复创建已有身份。</summary>
    Task<List<User>> GetExistingAsync(CancellationToken cancellationToken);

    void AddRange(IReadOnlyCollection<User> users);
}
