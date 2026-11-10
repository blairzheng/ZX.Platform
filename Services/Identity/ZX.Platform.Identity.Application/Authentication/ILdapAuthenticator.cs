namespace ZX.Platform.Identity.Application.Authentication;

/// <summary>
/// 员工凭 OA 工号和密码向目录认证。
/// 只有完成员工自身的 bind 后，才读取其目录身份并用于登录。
/// </summary>
public interface ILdapAuthenticator
{
    /// <summary>
    /// 配置是否启用。关闭时 OA 登录返回受控的不可用状态，本地登录不受影响。
    /// </summary>
    bool IsEnabled { get; }

    /// <summary>
    /// 认证员工凭据。
    /// </summary>
    /// <exception cref="LdapUnavailableException">目录不可用或认证后无法读取身份。</exception>
    /// <exception cref="LdapAmbiguousResultException">同一工号返回多条目录记录。</exception>
    /// <exception cref="LdapInvalidCredentialsException">工号不存在、密码错误或目录拒绝认证。</exception>
    Task<LdapDirectoryUser> AuthenticateAsync(
        string employeeNo,
        string password,
        CancellationToken cancellationToken);
}

/// <summary>
/// 目录明确拒绝了凭据：工号不存在、密码错误、账号锁定或密码过期。
/// 不触发主备切换，也不遍历多种 bind 格式，避免触发 AD 锁定。
/// </summary>
public sealed class LdapInvalidCredentialsException : Exception
{
    public LdapInvalidCredentialsException(string message) : base(message)
    {
    }
}

/// <summary>
/// 按工号限制 OA 登录尝试次数，键使用工号摘要，不保存密码。
/// 多个 Identity 实例共享计数，避免绕过。
/// </summary>
public interface ILdapLoginGuard
{
    /// <summary>
    /// 超限时抛出 429 业务异常。
    /// </summary>
    Task EnsureAllowedAsync(string employeeNo, CancellationToken cancellationToken);

    Task RecordFailureAsync(string employeeNo, CancellationToken cancellationToken);

    Task ResetAsync(string employeeNo, CancellationToken cancellationToken);
}
