using System.ComponentModel.DataAnnotations;
using ZX.Platform.Identity.Application.Authentication;

namespace ZX.Platform.Identity.Application.Users.Dtos;

/// <summary>
/// 管理员按工号查询目录的输入。
/// </summary>
public sealed class LdapUserLookupQueryDto
{
    [Required(ErrorMessage = "Validation.EmployeeNoRequired")]
    [MaxLength(32, ErrorMessage = "Validation.EmployeeNoMaxLength")]
    public string EmployeeNo { get; set; } = string.Empty;
}

/// <summary>
/// 管理员按工号查询目录的结果。不包含 DN、服务账号或目录内部地址。
/// </summary>
public sealed class LdapUserLookupResultDto
{
    /// <summary>
    /// 是否在目录中找到该工号。依赖故障不使用该结果表达。
    /// </summary>
    public bool Found { get; init; }

    public string EmployeeNo { get; init; } = string.Empty;

    public string? DisplayName { get; init; }

    public string? Department { get; init; }

    /// <summary>
    /// 目录账号启用状态，见 LdapDirectoryStatus。
    /// </summary>
    public string DirectoryStatus { get; init; } = LdapDirectoryStatus.Unknown;

    /// <summary>
    /// 服务端综合目录状态、必要身份字段和本地账号冲突后的结论。
    /// 前端只能用于展示，提交时服务端会重新查询。
    /// </summary>
    public bool CanProvision { get; init; }

    /// <summary>
    /// 不可开通原因，见 <see cref="LdapProvisionReasons"/>。
    /// </summary>
    public string? Reason { get; init; }
}

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
/// 管理员预开通 OA 用户。目录姓名和唯一标识由服务端查询，不接受客户端指定。
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
