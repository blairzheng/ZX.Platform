using System.ComponentModel.DataAnnotations;
using ZX.Platform.Identity.Application.Personnel;

namespace ZX.Platform.Identity.Application.Authentication;

/// <summary>固定目录桥接协议，不接受调用方指定目录节点、查询根或过滤器。</summary>
public static class DirectoryRelayProtocol
{
    public const string KeyHeader = "X-Directory-Key";
    public const string AuthenticatePath = "api/DirectoryRelay/authenticate";
    public const string PersonnelPath = "api/DirectoryRelay/personnel";
    public const string InvalidCredentialsReason = "DirectoryInvalidCredentials";
    public const string AmbiguousReason = "DirectoryAmbiguous";
    public const string UnavailableReason = "DirectoryUnavailable";
}

public sealed class DirectoryRelayLoginDto
{
    [Required(ErrorMessage = "Validation.EmployeeNoRequired")]
    [MaxLength(32, ErrorMessage = "Validation.EmployeeNoMaxLength")]
    public string EmployeeNo { get; set; } = string.Empty;

    [Required(ErrorMessage = "Validation.PasswordRequired")]
    [MaxLength(1024, ErrorMessage = "Validation.LdapPasswordMaxLength")]
    public string Password { get; set; } = string.Empty;
}

/// <summary>服务端一次完整读取的固定目录范围；身份数据仍由本机原有用例处理。</summary>
public sealed class DirectoryRelayPersonnelResult
{
    public required string Provider { get; init; }
    public required string BusinessUnit { get; init; }
    public required IReadOnlyList<OaDirectoryPerson> People { get; init; }
}
