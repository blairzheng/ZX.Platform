using ZX.Platform.Identity.Application.Authentication.Dtos;
using ZX.Platform.Identity.Application.Authorization;
using ZX.Platform.Identity.Application.Errors;
using ZX.Platform.Identity.Application.Exceptions;
using ZX.Platform.Identity.Application.Menus;
using ZX.Platform.Identity.Application.Persistence;
using ZX.Platform.Identity.Application.Security;
using System.Security.Cryptography;
using System.Text;
using ZX.Platform.Identity.Application.Users.Dtos;
using ZX.Platform.Identity.Domain.Entities;

namespace ZX.Platform.Identity.Application.Authentication;

public sealed class AuthService(
    IUserRepository users,
    IUnitOfWork unitOfWork,
    IPasswordHasher passwords,
    IAuthSessionStore sessions,
    AuthOptions options,
    TimeProvider timeProvider,
    IPermissionService permissions,
    IMenuService menus,
    IUserSbuRepository userSbus,
    ISbuRepository sbus,
    ILdapAuthenticator ldap,
    ILdapLoginGuard ldapGuard) : IAuthService
{
    public async Task<AuthTokens> LoginAsync(LoginDto input, CancellationToken cancellationToken)
    {
        var account = input.Account.Trim().ToUpperInvariant();
        var user = await users.GetByNormalizedAccountAsync(account, cancellationToken);
        if (user is null)
        {
            // 账号不存在也执行密码校验，减少通过响应耗时探测账号是否存在的机会。
            // 空哈希由密码服务使用占位哈希处理，校验结果始终为 false。
            passwords.Verify(input.Password, string.Empty);
            throw InvalidCredentials();
        }

        // OA 账号只能走 OA 登录。必须在密码校验和失败计数之前返回：
        // OA 账号没有本地密码，若继续往下走会必然走到 RecordLoginFailure，
        // 知道工号的人反复调用本地登录就能把这个人的目录登录锁在本地锁定里。
        if (user.AuthType != UserAuthType.Local)
        {
            // 仍然执行一次占位校验，避免响应耗时暴露该账号是 OA 账号。
            passwords.Verify(input.Password, string.Empty);
            throw InvalidCredentials();
        }

        var validPassword = passwords.Verify(input.Password, user.PasswordHash ?? string.Empty);
        var now = timeProvider.GetUtcNow();
        // 统一提示，不向未登录调用方区分账号不存在、禁用、删除或锁定。
        if (!user.CanLogin(now))
            throw InvalidCredentials();

        if (!validPassword)
        {
            user.RecordLoginFailure(now, options.MaximumFailedAttempts, TimeSpan.FromMinutes(options.LockoutMinutes));
            await unitOfWork.SaveChangesAsync(cancellationToken);
            throw InvalidCredentials();
        }

        user.RecordLoginSuccess();
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await sessions.CreateAsync(user.Id, user.Account, CredentialStamp(user), input.ReplaceExistingSession, cancellationToken)
            ?? throw new BusinessException(ErrorCodes.Conflict, "Auth.AlreadyOnline",
                new { Reason = "AlreadyOnline" });
    }

    public async Task<LdapLoginOutcome> LdapLoginAsync(LdapLoginDto input, CancellationToken cancellationToken)
    {
        var employeeNo = NormalizeEmployeeNo(input.EmployeeNo);
        if (!ldap.IsEnabled)
            throw new BusinessException(ErrorCodes.ServiceUnavailable, "Auth.LdapDisabled",
                new { Reason = LdapProvisionReasons.LdapDisabled });

        // 空密码在发往目录前拒绝，防止误触发匿名或未认证 bind。
        if (string.IsNullOrEmpty(input.Password))
            throw InvalidCredentials();

        await ldapGuard.EnsureAllowedAsync(employeeNo, cancellationToken);

        var identity = await AuthenticateDirectoryAsync(employeeNo, input.Password, cancellationToken);
        // 没有稳定的目录唯一标识就不能建立可复用身份，避免用姓名或可改名的 DN 冒充。
        if (string.IsNullOrWhiteSpace(identity.Subject))
            throw new BusinessException(ErrorCodes.ServiceUnavailable, "Auth.LdapUnavailable",
                new { Reason = LdapProvisionReasons.DirectoryIdentityIncomplete });
        if (identity.Status == LdapDirectoryStatus.Disabled)
            throw AccountUnavailable(LdapProvisionReasons.DirectoryDisabled);
        if (identity.Status != LdapDirectoryStatus.Enabled)
            throw new BusinessException(ErrorCodes.ServiceUnavailable, "Auth.LdapUnavailable",
                new { Reason = LdapProvisionReasons.DirectoryStateUnknown });

        await ldapGuard.ResetAsync(employeeNo, cancellationToken);

        var (user, created) = await ResolveOrCreateLdapUserAsync(identity, cancellationToken);
        EnsureLdapUserCanLogin(user, identity);

        var now = timeProvider.GetUtcNow();
        // 已启用但处于锁定期时不泄露具体原因，与本地登录保持一致。
        if (!user.IsEnabled)
            throw AccountUnavailable("AccountDisabled");
        if (user.IsLocked(now))
            throw AccountUnavailable("AccountUnavailable");

        // 只有真实 OA 认证通过、MOM 状态允许且两个身份字段均为空，才绑定预开通记录。
        // 不创建第二个账号，也不修改管理员预先分配的角色、SBU 和审批状态。
        if (user.LdapProvider is null && user.LdapSubject is null)
            user.BindLdapIdentity(identity.EmployeeNo, identity.Provider, identity.Subject!, identity.DisplayName);

        // 目录认证通过不代表绕过 ZX 的账号状态；这里只清除失败计数，不改变角色和 SBU。
        user.RecordLoginSuccess();
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (UniqueConstraintException)
        {
            throw new BusinessException(ErrorCodes.Conflict, "User.LdapIdentityConflict",
                new { Reason = LdapProvisionReasons.IdentityConflict, UserId = user.Id });
        }

        var tokens = await sessions.CreateAsync(user.Id, user.Account, CredentialStamp(user), input.ReplaceExistingSession, cancellationToken)
            ?? throw new BusinessException(ErrorCodes.Conflict, "Auth.AlreadyOnline",
                new { Reason = "AlreadyOnline" });

        return new LdapLoginOutcome(LdapLoginStatus.Authenticated, created, tokens);
    }

    public async Task<AuthTokens> RefreshAsync(string refreshToken, CancellationToken cancellationToken)
    {
        var session = await sessions.FindByRefreshTokenAsync(refreshToken, cancellationToken);
        if (session is null)
            throw InvalidSession();

        var user = await users.GetByIdAsync(session.UserId, cancellationToken);
        // Pending 可以维持受限会话；禁用、删除、拒绝及安全版本变化仍令会话失效。
        if (user is null || !user.CanUseSession ||
            session.CredentialStamp != CredentialStamp(user))
        {
            await sessions.RevokeAsync(session.UserId, session.SessionId, CancellationToken.None);
            throw InvalidSession();
        }

        return await sessions.RotateAsync(refreshToken, cancellationToken) ?? throw InvalidSession();
    }

    public async Task<CurrentUserDto> GetCurrentUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await users.GetByIdAsync(userId, cancellationToken);
        if (user is null || !user.CanUseSession)
            throw InvalidSession();

        // 不依赖“新账号碰巧没有关联”：待授权时强制清空所有业务能力和数据范围。
        if (!user.HasBusinessAccess)
            return new CurrentUserDto
            {
                Id = user.Id,
                Account = user.Account,
                DisplayName = user.DisplayName,
                AuthType = user.AuthType,
                ApprovalStatus = user.ApprovalStatus,
                HasBusinessAccess = false,
                CanChangePassword = user.CanChangePassword,
                IsAdministrator = false,
                Roles = [],
                Permissions = [],
                Sbus = [],
                Menus = []
            };

        var authorization = await permissions.GetAsync(userId, cancellationToken);
        var relations = await userSbus.GetByUserIdAsync(userId, cancellationToken);
        var enabledSbus = relations.Count == 0
            ? []
            : await sbus.GetEnabledSbusByIdsAsync(relations.Select(item => item.SbuId).ToList(), cancellationToken);
        var defaultSbuIds = relations.Where(item => item.IsDefault).Select(item => item.SbuId).ToHashSet();
        return new CurrentUserDto
        {
            Id = user.Id,
            Account = user.Account,
            DisplayName = user.DisplayName,
            AuthType = user.AuthType,
            ApprovalStatus = user.ApprovalStatus,
            HasBusinessAccess = user.HasBusinessAccess,
            CanChangePassword = user.CanChangePassword,
            IsAdministrator = authorization.IsAdministrator,
            Roles = authorization.Roles,
            Permissions = authorization.Permissions,
            Sbus = enabledSbus.OrderBy(item => item.Code).Select(item => new CurrentUserSbuDto
            {
                Id = item.Id,
                Code = item.Code,
                Name = item.Name,
                IsDefault = defaultSbuIds.Contains(item.Id)
            }).ToList(),
            Menus = await menus.GetCurrentAsync(authorization, cancellationToken)
        };
    }

    public async Task ChangePasswordAsync(Guid userId, ChangePasswordDto input, CancellationToken cancellationToken)
    {
        var user = await users.GetByIdAsync(userId, cancellationToken);
        if (user is null || !user.IsEnabled)
            throw InvalidSession();

        // OA 账号的密码由目录维护，ZX 不保存也不接受本地改密。
        if (!user.CanChangePassword)
            throw new BusinessException(ErrorCodes.Forbidden, "Auth.PasswordChangeNotAllowedForLdap",
                new { Reason = "LocalPasswordNotAllowed" });

        // 走到这里一定是本地账号，PasswordHash 不会为空；用空串兜底只为满足可空分析。
        if (!passwords.Verify(input.CurrentPassword, user.PasswordHash ?? string.Empty))
            throw new BusinessException(ErrorCodes.ValidationFailed, "Auth.CurrentPasswordIncorrect");

        user.ChangePasswordHash(passwords.Hash(input.NewPassword));
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            // 即使客户端断开或提交结果不确定，也尝试撤销；Redis 失败由 API 返回 503。
            await sessions.RevokeUserAsync(user.Id, CancellationToken.None);
        }
    }

    private async Task<LdapDirectoryUser> AuthenticateDirectoryAsync(
        string employeeNo, string password, CancellationToken cancellationToken)
    {
        try
        {
            return await ldap.AuthenticateAsync(employeeNo, password, cancellationToken);
        }
        catch (LdapInvalidCredentialsException)
        {
            // 只有目录明确拒绝凭据才计入失败次数，连接故障不能算密码错误。
            await ldapGuard.RecordFailureAsync(employeeNo, cancellationToken);
            await RecordLdapLoginFailureAsync(employeeNo, cancellationToken);
            throw InvalidCredentials();
        }
        catch (LdapUnavailableException)
        {
            throw new BusinessException(ErrorCodes.ServiceUnavailable, "Auth.LdapUnavailable",
                new { Reason = LdapProvisionReasons.LdapUnavailable });
        }
        catch (LdapAmbiguousResultException)
        {
            throw new BusinessException(ErrorCodes.ServiceUnavailable, "Auth.LdapUnavailable",
                new { Reason = LdapProvisionReasons.LdapAmbiguousResult });
        }
    }

    /// <summary>
    /// 按目录身份找现有账号；查不到时创建待审批账号。
    /// 并发首次登录由唯一索引裁决，冲突后重新读取真实状态。
    /// </summary>
    private async Task<(User User, bool Created)> ResolveOrCreateLdapUserAsync(
        LdapDirectoryUser identity, CancellationToken cancellationToken)
    {
        var existing = await FindByDirectoryIdentityAsync(identity, cancellationToken);
        if (existing is not null)
            return (existing, false);

        var accountOwner = await users.GetByNormalizedAccountAsync(
            identity.EmployeeNo.Trim().ToUpperInvariant(), cancellationToken);
        if (accountOwner is not null)
        {
            if (!accountOwner.ReleaseDeletedLocalAccountName())
                return (accountOwner, false);

            // 只在真实 OA 认证通过后兼容旧软删除记录。先保存释放结果，再插入新用户，
            // 避免同一次保存中 INSERT 早于 UPDATE 而撞上原账号的唯一索引。
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        // 目录登录名只用于目录认证；ZX 账号按约定固定取工号，且必须与 EMPLOYEE_NO 一致。
        var candidate = User.CreateLdap(
            identity.EmployeeNo, identity.EmployeeNo, identity.DisplayName, identity.Provider, identity.Subject!);
        try
        {
            users.Add(candidate);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return (candidate, true);
        }
        catch (UniqueConstraintException)
        {
            // 回滚跟踪状态后再读已创建的记录，不能吞掉异常当成功。
            users.Detach(candidate);
            var current = await FindByDirectoryIdentityAsync(identity, cancellationToken);
            if (current is not null)
                return (current, false);

            // 同工号或同账号已被本地账号占用时明确冲突，不覆盖、不合并。
            // ZX 账号就是工号，所以这里必须按工号查，而不是按目录登录名。
            var byAccount = await users.GetByNormalizedAccountAsync(
                identity.EmployeeNo.Trim().ToUpperInvariant(), cancellationToken);
            if (byAccount is not null)
                throw new BusinessException(ErrorCodes.Conflict, "User.AccountTypeConflict",
                    new { Reason = LdapProvisionReasons.AccountTypeConflict, UserId = byAccount.Id });

            throw new BusinessException(ErrorCodes.Conflict, "User.LdapIdentityConflict",
                new { Reason = LdapProvisionReasons.IdentityConflict });
        }
    }

    private async Task<User?> FindByDirectoryIdentityAsync(
        LdapDirectoryUser identity, CancellationToken cancellationToken) =>
        await users.GetByLdapIdentityAsync(identity.Provider, identity.Subject!, cancellationToken)
        ?? await users.GetByEmployeeNoAsync(identity.EmployeeNo, cancellationToken);

    private void EnsureLdapUserCanLogin(User user, LdapDirectoryUser identity)
    {
        // 已删除的 OA 工号和目录标识仍占位，不自动重建、恢复或重新审批。
        if (user.IsDeleted)
            throw AccountUnavailable("AccountUnavailable");

        // 同工号已经存在本地账号时按冲突处理，不改变原账号的认证类型和密码。
        if (!user.IsLdap)
            throw new BusinessException(ErrorCodes.Conflict, "User.AccountTypeConflict",
                new { Reason = LdapProvisionReasons.AccountTypeConflict, UserId = user.Id });

        var unbound = user.LdapProvider is null && user.LdapSubject is null;
        if ((!unbound &&
             (!string.Equals(user.LdapProvider, identity.Provider, StringComparison.OrdinalIgnoreCase) ||
              !string.Equals(user.LdapSubject, identity.Subject, StringComparison.OrdinalIgnoreCase))) ||
            !string.Equals(user.EmployeeNo, identity.EmployeeNo, StringComparison.Ordinal))
            throw new BusinessException(ErrorCodes.Conflict, "User.LdapIdentityConflict",
                new { Reason = LdapProvisionReasons.IdentityConflict, UserId = user.Id });

        if (user.ApprovalStatus == UserApprovalStatus.Rejected)
            throw new BusinessException(ErrorCodes.Forbidden, "Auth.ApprovalRejected",
                new { Reason = "ApprovalRejected" });
    }

    private async Task RecordLdapLoginFailureAsync(string employeeNo, CancellationToken cancellationToken)
    {
        var user = await users.GetByEmployeeNoAsync(employeeNo, cancellationToken);
        if (user is null || !user.IsLdap || user.IsDeleted)
            return;

        user.RecordLoginFailure(
            timeProvider.GetUtcNow(),
            options.MaximumFailedAttempts,
            TimeSpan.FromMinutes(options.LockoutMinutes));
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private static string NormalizeEmployeeNo(string employeeNo) => employeeNo.Trim();

    private static BusinessException InvalidCredentials() => new(ErrorCodes.Unauthorized, "Auth.InvalidCredentials");
    private static BusinessException InvalidSession() => new(ErrorCodes.Unauthorized, "Auth.SessionInvalid");

    private static BusinessException AccountUnavailable(string reason) =>
        new(ErrorCodes.Forbidden, "Auth.AccountUnavailable", new { Reason = reason });

    // 会话指纹同时覆盖账号身份和账户状态版本：OA 用户没有密码，不能再使用密码哈希；
    // 本地改密、审批、启用、禁用、删除和恢复都会改变版本，使旧会话失效。
    internal static string CredentialStamp(User user) =>
        "v2:" + Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes($"{user.Id:N}:{user.SecurityVersion}")));
}
