using System.Diagnostics;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using ZX.Platform.Identity.Api.Contracts;
using ZX.Platform.Identity.Api.Localization;
using ZX.Platform.Identity.Application.Authentication.Sso;
using ZX.Platform.Identity.Application.Errors;

namespace ZX.Platform.Identity.Api.Authentication;

// 独立的服务认证方案；MOM 用户 Bearer / Cookie 均不能调用兑换接口。
public sealed class GoFlowClientAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> schemes, ILoggerFactory logger, UrlEncoder encoder,
    GoFlowSsoOptions sso, IApiMessageLocalizer messages)
    : AuthenticationHandler<AuthenticationSchemeOptions>(schemes, logger, encoder)
{
    public const string SchemeName = "GoFlowClient";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!sso.Enabled || (!Request.IsHttps && !sso.AllowHttp))
            return Task.FromResult(AuthenticateResult.NoResult());

        var header = Request.Headers.Authorization;
        var value = header.ToString();
        if (header.Count != 1 || value.Length > 2048 ||
            !AuthenticationHeaderValue.TryParse(value, out var authorization) ||
            !string.Equals(authorization.Scheme, "Basic", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(authorization.Parameter))
            return Task.FromResult(AuthenticateResult.Fail("Invalid service credentials."));

        byte[] actual;
        try
        {
            actual = SHA256.HashData(Convert.FromBase64String(authorization.Parameter));
        }
        catch (FormatException)
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid service credentials."));
        }

        var expected = SHA256.HashData(Encoding.UTF8.GetBytes($"{sso.ClientId}:{sso.ClientSecret}"));
        if (!CryptographicOperations.FixedTimeEquals(actual, expected))
            return Task.FromResult(AuthenticateResult.Fail("Invalid service credentials."));

        var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, sso.ClientId)], SchemeName);
        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        var status = !sso.Enabled ? StatusCodes.Status503ServiceUnavailable :
            !Request.IsHttps && !sso.AllowHttp ? StatusCodes.Status403Forbidden : StatusCodes.Status401Unauthorized;
        var message = !sso.Enabled ? "Sso.Unavailable" : !Request.IsHttps && !sso.AllowHttp ? "Sso.HttpsRequired" : "Sso.ClientInvalid";
        var code = !sso.Enabled ? ErrorCodes.ServiceUnavailable :
            !Request.IsHttps && !sso.AllowHttp ? ErrorCodes.Forbidden : ErrorCodes.Unauthorized;
        Response.StatusCode = status;
        Response.Headers.CacheControl = "no-store";
        if (status == StatusCodes.Status401Unauthorized)
            Response.Headers.WWWAuthenticate = "Basic realm=\"MOM-GoFlow\", charset=\"UTF-8\"";
        return Response.WriteAsJsonAsync(new ApiResponse
        {
            Code = code,
            Message = messages[message].Value,
            TraceId = Activity.Current?.Id ?? Context.TraceIdentifier
        }, Context.RequestAborted);
    }
}
