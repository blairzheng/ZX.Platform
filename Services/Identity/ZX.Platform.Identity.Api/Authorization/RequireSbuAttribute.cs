using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using ZX.Platform.Identity.Application.Errors;
using ZX.Platform.Identity.Application.Exceptions;
using ZX.Platform.Identity.Application.Persistence;

namespace ZX.Platform.Identity.Api.Authorization;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RequireSbuAttribute : TypeFilterAttribute
{
    public RequireSbuAttribute() : base(typeof(RequireSbuFilter)) { }
}

public sealed class RequireSbuFilter(IUserSbuRepository userSbus, CurrentSbuContext currentSbu) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var request = context.HttpContext.Request;
        if (context.HttpContext.User.Identity?.IsAuthenticated != true ||
            !Guid.TryParse(context.HttpContext.User.FindFirstValue(JwtRegisteredClaimNames.Sub), out var userId))
            throw new BusinessException(ErrorCodes.Unauthorized, "Sbu.AuthenticationRequired");

        var header = request.Headers["X-SBU-ID"];
        if (header.Count != 1 || !Guid.TryParse(header[0], out var sbuId) || sbuId == Guid.Empty)
            throw new BusinessException(ErrorCodes.ValidationFailed, "Sbu.SelectionRequired");

        var sbu = await userSbus.GetEnabledSbuForUserAsync(userId, sbuId, request.HttpContext.RequestAborted);
        if (sbu is null)
            throw new BusinessException(ErrorCodes.Forbidden, "Sbu.AccessDenied");

        currentSbu.SetValidatedSbu(sbu.Id, sbu.Code);
        await next();
    }
}
