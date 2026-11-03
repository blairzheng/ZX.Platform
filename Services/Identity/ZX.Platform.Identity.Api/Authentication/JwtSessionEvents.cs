using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using ZX.Platform.Identity.Api.Contracts;
using ZX.Platform.Identity.Api.Localization;
using ZX.Platform.Identity.Application.Authentication;
using ZX.Platform.Identity.Application.Errors;

namespace ZX.Platform.Identity.Api.Authentication;

// JWT 格式、签名及有效期由 JwtBearer 验证；这里只检查共享会话并统一响应。
public sealed class JwtSessionEvents(IAuthSessionStore sessions, IApiMessageLocalizer messages) : JwtBearerEvents
{
    public const string SessionIdClaim = "sid";

    public override async Task TokenValidated(TokenValidatedContext context)
    {
        var user = context.Principal!;
        if (!Guid.TryParse(user.FindFirstValue(JwtRegisteredClaimNames.Sub), out var userId) ||
            !Guid.TryParse(user.FindFirstValue(SessionIdClaim), out var sessionId) ||
            string.IsNullOrWhiteSpace(user.Identity?.Name) ||
            user.FindFirstValue(JwtRegisteredClaimNames.Jti) is not string tokenId ||
            !await sessions.IsAccessTokenActiveAsync(userId, sessionId, tokenId, context.HttpContext.RequestAborted))
            context.Fail("Session expired or revoked.");
    }

    public override Task Challenge(JwtBearerChallengeContext context)
    {
        context.HandleResponse();
        context.Response.Headers.WWWAuthenticate = JwtBearerDefaults.AuthenticationScheme;
        return WriteFailureAsync(context.HttpContext, StatusCodes.Status401Unauthorized, ErrorCodes.Unauthorized, "Auth.SessionInvalid");
    }

    public override Task Forbidden(ForbiddenContext context) =>
        WriteFailureAsync(context.HttpContext, StatusCodes.Status403Forbidden, ErrorCodes.Forbidden, "Auth.Forbidden");

    private Task WriteFailureAsync(HttpContext context, int statusCode, int code, string messageKey)
    {
        context.Response.StatusCode = statusCode;
        context.Response.Headers.CacheControl = "no-store";
        return context.Response.WriteAsJsonAsync(new ApiResponse
        {
            Code = code,
            Message = messages[messageKey].Value,
            TraceId = Activity.Current?.Id ?? context.TraceIdentifier
        }, context.RequestAborted);
    }
}
