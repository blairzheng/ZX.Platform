using System.ComponentModel.DataAnnotations;

namespace ZX.Platform.Identity.Application.Authentication.Sso;

public sealed class IssueGoFlowTicketDto
{
    [Required(ErrorMessage = "Validation.SsoRequestInvalid"), MaxLength(2048, ErrorMessage = "Validation.SsoRequestInvalid")]
    public string RedirectUri { get; set; } = string.Empty;

    // 必须来自 GoFlow 发起的登录事务；只支持 PKCE S256。
    [Required(ErrorMessage = "Validation.SsoRequestInvalid"), RegularExpression("^[A-Za-z0-9_-]{43}$", ErrorMessage = "Validation.SsoRequestInvalid")]
    public string CodeChallenge { get; set; } = string.Empty;

    [Required(ErrorMessage = "Validation.SsoRequestInvalid"), RegularExpression("^[A-Za-z0-9_-]{32,128}$", ErrorMessage = "Validation.SsoRequestInvalid")]
    public string State { get; set; } = string.Empty;

    [Required(ErrorMessage = "Validation.SsoRequestInvalid")]
    public GoFlowSsoTargetDto Target { get; set; } = new();
}

public sealed class GoFlowSsoTargetDto
{
    [Required(ErrorMessage = "Validation.SsoRequestInvalid"), RegularExpression("^home$", ErrorMessage = "Validation.SsoRequestInvalid")]
    public string Page { get; set; } = "home";
}

public sealed class RedeemGoFlowTicketDto
{
    [Required(ErrorMessage = "Validation.SsoRequestInvalid"), RegularExpression("^[A-Za-z0-9_-]{43}$", ErrorMessage = "Validation.SsoRequestInvalid")]
    public string Ticket { get; set; } = string.Empty;

    [Required(ErrorMessage = "Validation.SsoRequestInvalid"), RegularExpression("^[A-Za-z0-9._~-]{43,128}$", ErrorMessage = "Validation.SsoRequestInvalid")]
    public string CodeVerifier { get; set; } = string.Empty;

    [Required(ErrorMessage = "Validation.SsoRequestInvalid"), MaxLength(2048, ErrorMessage = "Validation.SsoRequestInvalid")]
    public string RedirectUri { get; set; } = string.Empty;
}

public sealed record GoFlowTicketIssuedDto(string RedirectUrl, DateTimeOffset ExpiresAtUtc);

public sealed record GoFlowSsoConfigurationDto(bool Enabled, string? StartUrl, string? CallbackUri, bool AllowHttp);

// 仅兑换接口返回给已认证的 GoFlow 后端；不包含 MOM Token 或权限。
public sealed record GoFlowTicketIdentityDto(
    string ClientId, Guid MomUserId, string EmployeeNo, string DirectoryProvider,
    string DirectorySubject, string DisplayName, GoFlowSsoTargetDto Target);
