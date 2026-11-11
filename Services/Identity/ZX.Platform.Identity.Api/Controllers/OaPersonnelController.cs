using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ZX.Platform.Identity.Api.Contracts;
using ZX.Platform.Identity.Api.Localization;
using ZX.Platform.Identity.Application.Authorization;
using ZX.Platform.Identity.Application.Personnel;
using ZX.Platform.Identity.Application.Personnel.Dtos;

namespace ZX.Platform.Identity.Api.Controllers;

/// <summary>BU17 OA 用户同步及组织视图；不自动审批或授予角色、SBU。</summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public sealed class OaPersonnelController(IOaPersonnelService personnel, IApiMessageLocalizer messages) : ControllerBase
{
    [HttpGet("tree")]
    [Authorize(Policy = IdentityPermissions.OaPersonnelRead)]
    [ProducesResponseType(typeof(ApiResponse<OaPersonnelTreeDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTreeAsync(CancellationToken cancellationToken)
    {
        var result = await personnel.GetTreeAsync(cancellationToken);
        return Ok(new ApiResponse<OaPersonnelTreeDto>
        {
            Code = 0,
            Message = messages["OaPersonnel.TreeLoaded"].Value,
            Data = result
        });
    }

    [HttpPost("sync")]
    [Authorize(Policy = IdentityPermissions.OaPersonnelSync)]
    [ProducesResponseType(typeof(ApiResponse<OaPersonnelSyncResultDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> SyncAsync(CancellationToken cancellationToken)
    {
        var actorId = Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
        var result = await personnel.SyncAsync(actorId, cancellationToken);
        return Ok(new ApiResponse<OaPersonnelSyncResultDto>
        {
            Code = 0,
            Message = messages["OaPersonnel.Synced", result.AddedCount, result.SkippedCount,
                result.InvalidCount, result.ProfileAddedCount].Value,
            Data = result
        });
    }
}
