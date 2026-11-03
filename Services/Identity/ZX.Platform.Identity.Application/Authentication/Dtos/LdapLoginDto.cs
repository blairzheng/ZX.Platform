using System.ComponentModel.DataAnnotations;

namespace ZX.Platform.Identity.Application.Authentication.Dtos;

/// <summary>
/// OA 登录请求。工号按字符串处理，密码不 Trim、不保存，也不套用本地密码强度规则。
/// </summary>
public sealed class LdapLoginDto
{
    [Required(ErrorMessage = "Validation.EmployeeNoRequired")]
    [MaxLength(32, ErrorMessage = "Validation.EmployeeNoMaxLength")]
    public string EmployeeNo { get; set; } = string.Empty;

    [Required(ErrorMessage = "Validation.PasswordRequired")]
    [MaxLength(1024, ErrorMessage = "Validation.LdapPasswordMaxLength")]
    public string Password { get; set; } = string.Empty;

    public bool ReplaceExistingSession { get; set; }
}

/// <summary>
/// OA 登录结果。只有 <see cref="LdapLoginStatus.Authenticated"/> 才带 Token。
/// </summary>
public sealed class LdapLoginResultDto
{
    /// <summary>
    /// 业务结果，见 <see cref="LdapLoginStatus"/>。
    /// </summary>
    public required string Status { get; init; }

    /// <summary>
    /// 本次请求是否新建了待审批账号；正常登录时为 null。
    /// </summary>
    public bool? Created { get; init; }

    /// <summary>
    /// 登录成功时返回的访问令牌；待审批时不返回任何令牌。
    /// </summary>
    public AccessTokenDto? Tokens { get; init; }
}

/// <summary>
/// OA 登录的业务结果取值。
/// </summary>
public static class LdapLoginStatus
{
    /// <summary>
    /// 认证通过并已建立会话。
    /// </summary>
    public const string Authenticated = "Authenticated";

    /// <summary>
    /// 首次申请已提交或仍在等待审批，未建立会话。
    /// </summary>
    public const string PendingApproval = "PendingApproval";
}
