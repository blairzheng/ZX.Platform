using Microsoft.EntityFrameworkCore;
using ZX.Platform.Identity.Application.Personnel;
using ZX.Platform.Identity.Domain.Entities;

namespace ZX.Platform.Identity.Infrastructure.Persistence.Repositories;

public sealed class OaPersonnelUserRepository(PlatformIdentityDbContext dbContext) : IOaPersonnelUserRepository
{
    public Task<List<User>> GetAllAsync(string businessUnit, CancellationToken cancellationToken) =>
        dbContext.Users.AsNoTracking()
            .Where(user => user.AuthType == UserAuthType.Ldap &&
                user.OaBusinessUnit == businessUnit && user.OaSyncedAtUtc != null)
            .OrderBy(user => user.OaDepartmentName)
            .ThenBy(user => user.OaName)
            .ThenBy(user => user.Id)
            .ToListAsync(cancellationToken);

    public Task<List<User>> GetExistingAsync(CancellationToken cancellationToken) =>
        // 仅本用例做全局身份占位检查；不借同步恢复、绑定或启用已有用户。
        dbContext.Users.IgnoreQueryFilters().ToListAsync(cancellationToken);

    public void AddRange(IReadOnlyCollection<User> users) => dbContext.Users.AddRange(users);
}
