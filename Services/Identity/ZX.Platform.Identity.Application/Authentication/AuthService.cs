using ZX.Platform.Identity.Application.Authentication.Dtos;
using ZX.Platform.Identity.Application.Errors;
using ZX.Platform.Identity.Application.Exceptions;
using ZX.Platform.Identity.Application.Persistence;
using ZX.Platform.Identity.Application.Security;
using System.Security.Cryptography;
using System.Text;

namespace ZX.Platform.Identity.Application.Authentication;

public sealed class AuthService(
    IUserRepository users,
    IUnitOfWork unitOfWork,
    IPasswordHasher passwords,
    IAuthSessionStore sessions,
    AuthOptions options,
    TimeProvider timeProvider) : IAuthService
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

        var validPassword = passwords.Verify(input.Password, user.PasswordHash);
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
        return await sessions.CreateAsync(user.Id, user.Account, CredentialStamp(user.PasswordHash), input.ReplaceExistingSession, cancellationToken)
            ?? throw new BusinessException(ErrorCodes.Conflict, "Auth.AlreadyOnline",
                new { Reason = "AlreadyOnline" });
    }

    public async Task<AuthTokens> RefreshAsync(string refreshToken, CancellationToken cancellationToken)
    {
        var session = await sessions.FindByRefreshTokenAsync(refreshToken, cancellationToken);
        if (session is null)
            throw InvalidSession();

        var user = await users.GetByIdAsync(session.UserId, cancellationToken);
        if (user is null || !user.IsEnabled || session.CredentialStamp != CredentialStamp(user.PasswordHash))
        {
            await sessions.RevokeAsync(session.UserId, session.SessionId, CancellationToken.None);
            throw InvalidSession();
        }

        return await sessions.RotateAsync(refreshToken, cancellationToken) ?? throw InvalidSession();
    }

    public async Task<CurrentUserDto> GetCurrentUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await users.GetByIdAsync(userId, cancellationToken);
        if (user is null || !user.IsEnabled)
            throw InvalidSession();

        return new CurrentUserDto
        {
            Id = user.Id,
            Account = user.Account,
            DisplayName = user.DisplayName,
            IsAdministrator = options.IsAdministrator(user.Account)
        };
    }

    public async Task ChangePasswordAsync(Guid userId, ChangePasswordDto input, CancellationToken cancellationToken)
    {
        var user = await users.GetByIdAsync(userId, cancellationToken);
        if (user is null || !user.IsEnabled)
            throw InvalidSession();

        if (!passwords.Verify(input.CurrentPassword, user.PasswordHash))
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

    private static BusinessException InvalidCredentials() => new(ErrorCodes.Unauthorized, "Auth.InvalidCredentials");
    private static BusinessException InvalidSession() => new(ErrorCodes.Unauthorized, "Auth.SessionInvalid");

    // 刷新时识别并发改密留下的旧密码会话；Redis 不保存 BCrypt 哈希或明文密码。
    private static string CredentialStamp(string passwordHash) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(passwordHash)));
}
