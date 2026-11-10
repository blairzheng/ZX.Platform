using System.ComponentModel.DataAnnotations;
using ZX.Platform.Identity.Application.Authentication;

namespace ZX.Platform.Identity.Infrastructure.Security;

/// <summary>
/// OA / LDAP 目录配置。员工使用自己的 OA 密码认证，不配置独立查询账号。
/// </summary>
public sealed class LdapOptions
{
    /// <summary>
    /// 是否启用 OA 登录。关闭时本地登录不受影响，OA 相关接口返回受控的不可用状态。
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// 目录来源标识。更换该值等同于更换身份命名空间，不能随部署随机修改。
    /// </summary>
    [Required]
    [MaxLength(32)]
    public string Provider { get; set; } = "luxshare-ad";

    public List<LdapServerOptions> Servers { get; set; } = [];

    /// <summary>
    /// 用户查询根节点。只在 Enabled=true 时必填，关闭时允许为空。
    /// </summary>
    public string BaseDn { get; set; } = string.Empty;

    /// <summary>
    /// AD 的 NetBIOS 域名。使用 域名\工号 直接认证，默认 LUXSHARE。
    /// </summary>
    [Required]
    public string Domain { get; set; } = "LUXSHARE";

    /// <summary>
    /// 查询用户使用的属性，通常是 sAMAccountName。
    /// </summary>
    [Required]
    public string UserNameAttribute { get; set; } = "sAMAccountName";

    /// <summary>
    /// 工号属性。以 IT 确认的目录定义为准，工号始终按字符串处理。
    /// 修改该值会同时改变输入工号的查询属性（见 <see cref="ResolveEmployeeLookupAttribute"/>）。
    /// </summary>
    [Required]
    public string EmployeeNumberAttribute { get; set; } = "sAMAccountName";

    /// <summary>
    /// 输入工号时用于搜索的属性。留空表示跟随 <see cref="EmployeeNumberAttribute"/>，
    /// 只有“用登录名查询、工号另存其他属性”时才需要单独指定。
    /// </summary>
    public string EmployeeLookupAttribute { get; set; } = string.Empty;

    /// <summary>
    /// 稳定身份标识属性，默认使用 AD objectGUID。
    /// </summary>
    [Required]
    public string SubjectAttribute { get; set; } = "objectGUID";

    public string DisplayNameAttribute { get; set; } = "cn";

    public string DepartmentAttribute { get; set; } = "department";

    public string StatusAttribute { get; set; } = "userAccountControl";

    [Range(1, 30)]
    public int ConnectTimeoutSeconds { get; set; } = 3;

    [Range(1, 60)]
    public int OperationTimeoutSeconds { get; set; } = 5;

    /// <summary>
    /// 单次登录或查询的总预算，包含主备节点切换。
    /// </summary>
    [Range(1, 120)]
    public int TotalTimeoutSeconds { get; set; } = 15;

    /// <summary>
    /// 每实例并发执行的目录操作上限，避免同步 LDAP 调用占满线程池。
    /// </summary>
    [Range(1, 64)]
    public int MaxConcurrentOperations { get; set; } = 8;

    /// <summary>
    /// 保留该键只为让误配置在启动时显式失败；不允许关闭证书校验。
    /// 证书问题应通过受信任 CA 和正确的服务器名称解决，而不是放行任意证书。
    /// </summary>
    public bool VerifyServerCertificate { get; set; } = true;

    /// <summary>
    /// 是否允许明文 389 连接。必须在运维明确记录后才可开启。
    /// </summary>
    public bool AllowPlainText { get; set; }

    /// <summary>
    /// 同一工号在窗口内允许的失败次数，超过后返回 429，避免触发 AD 锁定。
    /// </summary>
    [Range(1, 100)]
    public int MaxFailedAttempts { get; set; } = 10;

    [Range(30, 86400)]
    public int FailureWindowSeconds { get; set; } = 300;

    /// <summary>
    /// 输入工号时实际用于搜索的属性。未单独配置时跟随工号属性，
    /// 保证“改了工号属性，查询也一起改”，不会停留在登录名上。
    /// </summary>
    public string ResolveEmployeeLookupAttribute() =>
        !string.IsNullOrWhiteSpace(EmployeeLookupAttribute) ? EmployeeLookupAttribute.Trim()
        : !string.IsNullOrWhiteSpace(EmployeeNumberAttribute) ? EmployeeNumberAttribute.Trim()
        : UserNameAttribute.Trim();

    /// <summary>
    /// 启用时做本地校验；不连接远端，LDAP 不可达不能阻止 Identity 启动。
    /// </summary>
    public void Validate()
    {
        if (!Enabled)
            return;

        if (Servers.Count == 0)
            throw new InvalidOperationException("AuthenticationLdap:Servers 至少需要配置一个目录节点。");
        if (string.IsNullOrWhiteSpace(BaseDn))
            throw new InvalidOperationException("AuthenticationLdap:BaseDn 不能为空。");
        if (string.IsNullOrWhiteSpace(Domain) || Domain.Length > 15 ||
            Domain.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '-'))
            throw new InvalidOperationException("AuthenticationLdap:Domain 必须是有效的 AD NetBIOS 域名。");
        if (!string.Equals(UserNameAttribute, "sAMAccountName", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("AuthenticationLdap:UserNameAttribute 必须为 sAMAccountName，与直接认证的登录名一致。");

        // 放行任意证书等于放弃 TLS 的身份保证；实施方案明确不允许该配置存在。
        if (!VerifyServerCertificate)
            throw new InvalidOperationException(
                "AuthenticationLdap:VerifyServerCertificate 不允许设置为 false；请通过受信任 CA 和正确的服务器名称解决证书问题。");

        foreach (var server in Servers)
        {
            if (string.IsNullOrWhiteSpace(server.Host))
                throw new InvalidOperationException("AuthenticationLdap:Servers 缺少 Host。");
            if (server.Port is <= 0 or > 65535)
                throw new InvalidOperationException("AuthenticationLdap:Servers 的 Port 必须在 1-65535 之间。");
            if (!LdapEncryption.IsValid(server.Encryption))
                throw new InvalidOperationException("AuthenticationLdap:Servers 的 Encryption 只能是 Plain、Ldaps 或 StartTls。");
            if (server.Encryption == LdapEncryption.Plain && !AllowPlainText)
                throw new InvalidOperationException(
                    "AuthenticationLdap 使用 Plain 传输时必须显式设置 AllowPlainText=true；请优先配置 TLS。");
        }
    }
}

public sealed class LdapServerOptions
{
    [Required]
    public string Host { get; set; } = string.Empty;

    [Range(1, 65535)]
    public int Port { get; set; } = 389;

    /// <summary>
    /// 传输方式，取值见 <see cref="LdapEncryption"/>。
    /// </summary>
    public string Encryption { get; set; } = LdapEncryption.Plain;
}

public static class LdapEncryption
{
    public const string Plain = "Plain";
    public const string Ldaps = "Ldaps";
    public const string StartTls = "StartTls";

    public static bool IsValid(string? value) => value is Plain or Ldaps or StartTls;
}
