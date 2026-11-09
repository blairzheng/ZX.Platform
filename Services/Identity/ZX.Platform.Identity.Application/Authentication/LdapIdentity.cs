namespace ZX.Platform.Identity.Application.Authentication;

/// <summary>
/// 目录账号在 LDAP 中的启用状态。Unknown 表示没有读到可信状态，不能据此开通。
/// </summary>
public static class LdapDirectoryStatus
{
    public const string Enabled = "Enabled";
    public const string Disabled = "Disabled";
    public const string Unknown = "Unknown";
}

/// <summary>
/// 完成员工密码认证后读取的可信目录身份。
/// 只有在完成用户 bind 后，登录流程才可以使用其中的身份字段。
/// </summary>
/// <param name="EmployeeNo">工号，既是查询键也是 ZX 账号的唯一来源。</param>
/// <param name="Account">
/// 目录登录名（通常是 sAMAccountName），只用于目录侧识别；ZX 账号不使用该值，
/// 避免同一个人的 ZX 账号随目录改名而漂移。
/// </param>
public sealed record LdapDirectoryUser(
    string EmployeeNo,
    string Account,
    string DisplayName,
    string? Department,
    string Provider,
    string? Subject,
    string Status);

/// <summary>
/// 目录依赖不可用：连接失败、超时、认证后读取身份失败或权限不足。
/// 必须映射为 503，不能解释成工号不存在或密码错误。
/// </summary>
public sealed class LdapUnavailableException : Exception
{
    public LdapUnavailableException(string reason, Exception? innerException = null)
        : base(reason, innerException)
    {
        Reason = reason;
    }

    public string Reason { get; }
}

/// <summary>
/// 目录返回多条或零条身份记录中的多条结果，属于目录配置异常。
/// </summary>
public sealed class LdapAmbiguousResultException : Exception
{
    public LdapAmbiguousResultException(string message) : base(message)
    {
    }
}
