using System.ComponentModel.DataAnnotations;

namespace ZX.Platform.Identity.Application.Users.Dtos;

/// <summary>
/// 预开通不可执行的结构化原因。
/// </summary>
public static class LdapProvisionReasons
{
    public const string LdapUserNotFound = "LdapUserNotFound";
    public const string DirectoryDisabled = "DirectoryDisabled";
    public const string DirectoryStateUnknown = "DirectoryStateUnknown";
    public const string DirectoryIdentityIncomplete = "DirectoryIdentityIncomplete";
    public const string AccountExists = "AccountExists";
    public const string AccountTypeConflict = "AccountTypeConflict";
    public const string IdentityConflict = "IdentityConflict";
    public const string LdapAmbiguousResult = "LdapAmbiguousResult";
    public const string LdapDisabled = "LdapDisabled";
    public const string LdapUnavailable = "LdapUnavailable";
}

/// <summary>
/// 管理员按工号预开通 OA 用户，不查询 LDAP。目录姓名和唯一标识在首次 OA 认证后补齐。
/// </summary>
public sealed class CreateLdapUserDto
{
    [Required(ErrorMessage = "Validation.EmployeeNoRequired")]
    [MaxLength(32, ErrorMessage = "Validation.EmployeeNoMaxLength")]
    public string EmployeeNo { get; set; } = string.Empty;

    /// <summary>
    /// 是否一次完成审批和授权。为 false 时只创建待审批记录，且不能携带角色或 SBU。
    /// </summary>
    public bool OpenImmediately { get; set; }

    public List<Guid> RoleIds { get; set; } = [];

    public List<Guid> SbuIds { get; set; } = [];

    [MaxLength(500, ErrorMessage = "Validation.ReviewReasonMaxLength")]
    public string? Reason { get; set; }
}

/// <summary>
/// 预开通结果，前端据此明确展示已开通或待审批。
/// </summary>
public sealed class LdapUserCreatedDto
{
    public Guid Id { get; init; }
    public string ApprovalStatus { get; init; } = string.Empty;
    public bool IsEnabled { get; init; }
    public long SecurityVersion { get; init; }
}

/// <summary>
/// 审批通过 OA 账号并在同一事务中分配角色和 SBU。
/// </summary>
public sealed class ApproveLdapUserDto
{
    [Required(ErrorMessage = "Validation.UserRequired")]
    public Guid Id { get; set; }

    public List<Guid> RoleIds { get; set; } = [];

    public List<Guid> SbuIds { get; set; } = [];

    /// <summary>
    /// 前端读取到的安全版本；与数据库不一致时返回 409，不静默覆盖。
    /// </summary>
    [Required(ErrorMessage = "Validation.SecurityVersionRequired")]
    public long? ExpectedSecurityVersion { get; set; }

    [MaxLength(500, ErrorMessage = "Validation.ReviewReasonMaxLength")]
    public string? Reason { get; set; }
}

/// <summary>
/// 拒绝 OA 开通申请。
/// </summary>
public sealed class RejectLdapUserDto
{
    [Required(ErrorMessage = "Validation.UserRequired")]
    public Guid Id { get; set; }

    [Required(ErrorMessage = "Validation.SecurityVersionRequired")]
    public long? ExpectedSecurityVersion { get; set; }

    [MaxLength(500, ErrorMessage = "Validation.ReviewReasonMaxLength")]
    public string? Reason { get; set; }
}
