namespace ZX.Platform.Identity.Api.Options;

public sealed class AuthCookieOptions
{
    public const string CookieName = "zx_identity_refresh";
    public const string CookiePath = "/api";
    public bool Secure { get; set; } = true;
}
