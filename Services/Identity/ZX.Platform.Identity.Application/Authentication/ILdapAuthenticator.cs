namespace ZX.Platform.Identity.Application.Authentication;

/// <summary>
/// 员工凭 OA 工号和密码向目录认证。
/// 只有完成用户 bind 后返回的身份才可以用于登录，服务账号查到用户不代表认证通过。
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
    /// <exception cref="LdapUnavailableException">目录不可用或服务账号无法完成查询。</exception>
    /// <exception cref="LdapAmbiguousResultException">同一工号返回多条目录记录。</exception>
    /// <exception cref="LdapInvalidCredentialsException">工号不存在、密码错误或目录拒绝认证。</exception>
    Task<LdapDirectoryUser> AuthenticateAsync(
        string employeeNo,
        string password,
        CancellationToken cancellationToken);
}

/// <summary>
/// 管理员按工号只读查询目录，用于预开通前的核对。
/// 不做员工密码 bind，不代表该员工已经登录。
/// </summary>
public interface ILdapDirectory
{
    bool IsEnabled { get; }

    /// <summary>
    /// 按工号精确查询目录用户；成功完成搜索但没有结果时返回 null。
    /// </summary>
    /// <exception cref="LdapUnavailableException">目录不可用或服务账号无法完成查询。</exception>
    /// <exception cref="LdapAmbiguousResultException">同一工号返回多条目录记录。</exception>
    Task<LdapDirectoryUser?> FindByEmployeeNoAsync(
        string employeeNo,
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
