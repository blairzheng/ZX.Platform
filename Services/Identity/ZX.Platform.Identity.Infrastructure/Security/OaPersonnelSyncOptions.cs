namespace ZX.Platform.Identity.Infrastructure.Security;

/// <summary>
/// OA 人员目录批量读取配置。服务账号只用于此只读同步，不参与员工 OA 密码认证。
/// 凭据由服务端 User Secrets 或受保护的环境变量提供。
/// </summary>
public sealed class OaPersonnelSyncOptions
{
    public bool Enabled { get; set; }

    public string BindUser { get; set; } = string.Empty;

    public string BindPassword { get; set; } = string.Empty;

    /// <summary>匹配 department 最后一个逗号分段，不能由 HTTP 请求覆盖。</summary>
    public string BusinessUnit { get; set; } = "BU17";

    public int PageSize { get; set; } = 200;

    /// <summary>超过此数量整次读取失败，禁止把截断结果当作完整同步。</summary>
    public int MaxPeople { get; set; } = 10000;

    /// <summary>包含排队、连接、分页和主备切换的总预算。</summary>
    public int TotalTimeoutSeconds { get; set; } = 90;
}
