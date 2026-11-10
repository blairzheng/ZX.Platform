using System.ComponentModel.DataAnnotations;

namespace ZX.Platform.Identity.Application.Authentication.Sso;

public sealed class GoFlowSsoOptions
{
    public bool Enabled { get; set; }

    // 仅在明确批准的内网 HTTP 联调环境启用，默认保持 HTTPS。
    public bool AllowHttp { get; set; }

    [Required, RegularExpression("^[A-Za-z0-9._-]{1,100}$")]
    public string ClientId { get; set; } = "goflow";

    // 仅保存在 User Secrets / 部署变量，不与任何系统的 JWT 签名密钥共用。
    public string ClientSecret { get; set; } = string.Empty;

    [MaxLength(2048)]
    public string CallbackUri { get; set; } = string.Empty;

    public string StartUri { get; set; } = string.Empty;

    [Range(30, 120)]
    public int TicketLifetimeSeconds { get; set; } = 60;

    public void Validate()
    {
        Validator.ValidateObject(this, new ValidationContext(this), validateAllProperties: true);
        if (!Enabled) return;

        if (ClientSecret.Length is < 32 or > 256 || ClientSecret.Any(char.IsControl))
            throw new InvalidOperationException("SingleSignOn:GoFlow:ClientSecret must contain 32 to 256 characters without control characters.");
        if (!Uri.TryCreate(CallbackUri, UriKind.Absolute, out var uri) ||
            !IsAllowedScheme(uri) || string.IsNullOrEmpty(uri.Host) ||
            !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment))
            throw new InvalidOperationException("SingleSignOn:GoFlow:CallbackUri must be an absolute HTTPS callback (or HTTP with AllowHttp enabled) without credentials, query or fragment.");
        if (!Uri.TryCreate(StartUri, UriKind.Absolute, out var start) ||
            !IsAllowedScheme(start) || !string.IsNullOrEmpty(start.UserInfo) ||
            !string.IsNullOrEmpty(start.Query) || !string.IsNullOrEmpty(start.Fragment) ||
            start.GetLeftPart(UriPartial.Authority) != uri.GetLeftPart(UriPartial.Authority) ||
            start.AbsolutePath != "/api/auth/sso/mom/start" || uri.AbsolutePath != "/api/auth/sso/mom/callback")
            throw new InvalidOperationException("GoFlow start and callback must use the same allowed origin and the fixed /api/auth/sso/mom routes.");
    }
    private bool IsAllowedScheme(Uri uri) => uri.Scheme == Uri.UriSchemeHttps
        || (AllowHttp && uri.Scheme == Uri.UriSchemeHttp);
}
