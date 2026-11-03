using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ZX.Platform.Identity.Api.Authentication;
using ZX.Platform.Identity.Api.Contracts;
using ZX.Platform.Identity.Api.Localization;
using ZX.Platform.Identity.Api.Options;
using ZX.Platform.Identity.Application.Authentication;
using ZX.Platform.Identity.Application.Authentication.Dtos;
using ZX.Platform.Identity.Application.Errors;
using ZX.Platform.Identity.Application.Exceptions;

namespace ZX.Platform.Identity.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
[RequireIdentityCsrfHeader]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AuthController(IAuthService auth, IAuthSessionStore sessions,
    AuthCookieOptions cookieOptions, IApiMessageLocalizer messages) : ControllerBase
{
    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<IActionResult> LoginAsync([FromBody] LoginDto input, CancellationToken cancellationToken)
    {
        var tokens = await auth.LoginAsync(input, cancellationToken);
        SetRefreshCookie(tokens);
        return TokenResponse(tokens, "Auth.LoggedIn");
    }

    /// <summary>
    /// OA 员工登录。认证成功且账号状态允许登录时写入 Refresh Cookie；
    /// 待业务审批账号也建立会话，业务能力由 me 和各接口授权独立控制。
    /// </summary>
    [AllowAnonymous]
    [HttpPost("ldapLogin")]
    public async Task<IActionResult> LdapLoginAsync([FromBody] LdapLoginDto input, CancellationToken cancellationToken)
    {
        var outcome = await auth.LdapLoginAsync(input, cancellationToken);
        var tokens = outcome.Tokens;
        SetRefreshCookie(tokens);
        return Ok(new ApiResponse<LdapLoginResultDto>
        {
            Code = 0,
            Message = messages["Auth.LoggedIn"].Value,
            Data = new LdapLoginResultDto
            {
                Status = outcome.Status,
                Created = outcome.Created,
                Tokens = new AccessTokenDto
                {
                    AccessToken = tokens.AccessToken,
                    AccessTokenExpiresAtUtc = tokens.AccessTokenExpiresAtUtc,
                    RefreshTokenExpiresAtUtc = tokens.RefreshTokenExpiresAtUtc
                }
            }
        });
    }

    [AllowAnonymous]
    [HttpPost("refresh")]
    public async Task<IActionResult> RefreshAsync(CancellationToken cancellationToken)
    {
        try
        {
            var tokens = await auth.RefreshAsync(Request.Cookies[AuthCookieOptions.CookieName] ?? string.Empty, cancellationToken);
            SetRefreshCookie(tokens);
            return TokenResponse(tokens, "Auth.Refreshed");
        }
        catch (BusinessException exception) when (exception.Code == ErrorCodes.Unauthorized)
        {
            ClearRefreshCookie();
            // Challenge 写统一 401，保留删除 Cookie 的响应头。
            return Challenge();
        }
    }

    [AllowAnonymous]
    [HttpPost("logout")]
    public async Task<IActionResult> LogoutAsync(CancellationToken cancellationToken)
    {
        var refreshSession = await sessions.FindByRefreshTokenAsync(
            Request.Cookies[AuthCookieOptions.CookieName] ?? string.Empty, cancellationToken);
        if (refreshSession is not null)
            await sessions.RevokeAsync(refreshSession.UserId, refreshSession.SessionId, CancellationToken.None);

        // API 调用方没有 Cookie 时，也可以用有效的 Bearer 退出。
        if (Guid.TryParse(User.FindFirstValue(JwtSessionEvents.SessionIdClaim), out var sessionId))
            await sessions.RevokeAsync(CurrentUserId, sessionId, CancellationToken.None);

        ClearRefreshCookie();
        return Ok(new ApiResponse { Code = 0, Message = messages["Auth.LoggedOut"].Value });
    }

    [HttpGet("me")]
    public async Task<IActionResult> GetCurrentUserAsync(CancellationToken cancellationToken)
    {
        var current = await auth.GetCurrentUserAsync(CurrentUserId, cancellationToken);
        return Ok(new ApiResponse<CurrentUserDto> { Code = 0, Message = messages["Auth.CurrentUserLoaded"].Value, Data = current });
    }

    [HttpPut("changePassword")]
    public async Task<IActionResult> ChangePasswordAsync([FromBody] ChangePasswordDto input, CancellationToken cancellationToken)
    {
        await auth.ChangePasswordAsync(CurrentUserId, input, cancellationToken);
        ClearRefreshCookie();
        return Ok(new ApiResponse { Code = 0, Message = messages["Auth.PasswordChanged"].Value });
    }

    private Guid CurrentUserId => Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);

    private IActionResult TokenResponse(AuthTokens tokens, string messageKey) => Ok(new ApiResponse<AccessTokenDto>
    {
        Code = 0,
        Message = messages[messageKey].Value,
        Data = new AccessTokenDto
        {
            AccessToken = tokens.AccessToken,
            AccessTokenExpiresAtUtc = tokens.AccessTokenExpiresAtUtc,
            RefreshTokenExpiresAtUtc = tokens.RefreshTokenExpiresAtUtc
        }
    });

    private CookieOptions CookieSettings() => new()
    {
        HttpOnly = true,
        Secure = cookieOptions.Secure,
        SameSite = SameSiteMode.Strict,
        Path = AuthCookieOptions.CookiePath,
        IsEssential = true
    };

    private void SetRefreshCookie(AuthTokens tokens)
    {
        var settings = CookieSettings();
        settings.Expires = tokens.RefreshTokenExpiresAtUtc;
        Response.Cookies.Append(AuthCookieOptions.CookieName, tokens.RefreshToken, settings);
    }

    private void ClearRefreshCookie() => Response.Cookies.Delete(AuthCookieOptions.CookieName, CookieSettings());
}
