using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ZX.Platform.Identity.Api.Authentication;
using ZX.Platform.Identity.Api.Contracts;
using ZX.Platform.Identity.Api.Localization;
using ZX.Platform.Identity.Application.Authentication.Sso;
using ZX.Platform.Identity.Application.Authorization;
using ZX.Platform.Identity.Application.Errors;
using ZX.Platform.Identity.Application.Exceptions;

namespace ZX.Platform.Identity.Api.Controllers;

[ApiController]
[Route("api/sso/goflow")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class GoFlowSsoController(IGoFlowSsoService sso, GoFlowSsoOptions options, IApiMessageLocalizer messages) : ControllerBase
{
    [HttpGet("configuration")]
    [Authorize]
    public IActionResult Configuration() => Ok(new ApiResponse<GoFlowSsoConfigurationDto>
    {
        Code = 0,
        Data = new(options.Enabled, options.Enabled ? options.StartUri : null,
            options.Enabled ? options.CallbackUri : null, options.Enabled && options.AllowHttp),
        Message = messages["Sso.ConfigurationLoaded"].Value
    });

    [HttpPost("tickets")]
    [Authorize(Policy = IdentityPermissions.GoFlowOpen)]
    [RequireIdentityCsrfHeader]
    [ProducesResponseType(typeof(ApiResponse<GoFlowTicketIssuedDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> IssueAsync([FromBody] IssueGoFlowTicketDto input, CancellationToken cancellationToken)
    {
        if (!Request.IsHttps && !options.AllowHttp)
            throw new BusinessException(ErrorCodes.Forbidden, "Sso.HttpsRequired");
        if (!Guid.TryParse(User.FindFirstValue(JwtRegisteredClaimNames.Sub), out var userId) ||
            !Guid.TryParse(User.FindFirstValue(JwtSessionEvents.SessionIdClaim), out var sessionId))
            throw new BusinessException(ErrorCodes.Unauthorized, "Auth.SessionInvalid");

        var result = await sso.IssueAsync(userId, sessionId, input, cancellationToken);
        return Ok(new ApiResponse<GoFlowTicketIssuedDto>
        {
            Code = 0, Message = messages["Sso.TicketIssued"].Value, Data = result
        });
    }

    [HttpPost("redeem")]
    [Authorize(AuthenticationSchemes = GoFlowClientAuthenticationHandler.SchemeName)]
    [ProducesResponseType(typeof(ApiResponse<GoFlowTicketIdentityDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> RedeemAsync([FromBody] RedeemGoFlowTicketDto input, CancellationToken cancellationToken)
    {
        var result = await sso.RedeemAsync(User.FindFirstValue(ClaimTypes.NameIdentifier)!, input, cancellationToken);
        return Ok(new ApiResponse<GoFlowTicketIdentityDto>
        {
            Code = 0, Message = messages["Sso.TicketRedeemed"].Value, Data = result
        });
    }
}
