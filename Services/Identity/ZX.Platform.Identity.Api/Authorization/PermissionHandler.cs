using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using ZX.Platform.Identity.Application.Authorization;

namespace ZX.Platform.Identity.Api.Authorization;

public sealed class PermissionHandler(IPermissionService permissions) : AuthorizationHandler<PermissionRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated != true ||
            !Guid.TryParse(context.User.FindFirstValue(JwtRegisteredClaimNames.Sub), out var userId)) return;
        var cancellationToken = (context.Resource as HttpContext)?.RequestAborted ?? CancellationToken.None;
        foreach (var code in requirement.Codes)
        {
            if (!await permissions.HasAsync(userId, code, cancellationToken)) continue;
            context.Succeed(requirement);
            break;
        }
    }
}
