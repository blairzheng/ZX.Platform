using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using ZX.Platform.Identity.Api.Authorization;
using ZX.Platform.Identity.Application.Errors;
using ZX.Platform.Identity.Domain.Entities;

namespace ZX.Platform.Identity.AuthChecks;

internal static class SbuScopeChecks
{
    public static async Task RunAsync()
    {
        var userId = Guid.NewGuid();
        var sbu = Sbu.Create("SBU2", "SBU 2");
        var sbuId = sbu.Id;
        var repository = new TestUserSbus();

        async Task<(bool Called, CurrentSbuContext Selection)> ExecuteAsync(string? header, Guid? user = null)
        {
            var http = new DefaultHttpContext();
            if (user != Guid.Empty)
                http.User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(JwtRegisteredClaimNames.Sub, (user ?? userId).ToString())], "Test"));
            if (header is not null) http.Request.Headers["X-SBU-ID"] = header;
            var action = new ActionContext(http, new RouteData(), new ControllerActionDescriptor());
            var executing = new ActionExecutingContext(action, [], new Dictionary<string, object?>(), new object());
            var selection = new CurrentSbuContext();
            var called = false;
            await new RequireSbuFilter(repository, selection).OnActionExecutionAsync(executing, () =>
            {
                called = true;
                return Task.FromResult(new ActionExecutedContext(action, [], new object()));
            });
            return (called, selection);
        }

        await Check.ErrorAsync(async () => await ExecuteAsync(sbuId.ToString(), Guid.Empty), ErrorCodes.Unauthorized);
        await Check.ErrorAsync(async () => await ExecuteAsync(null), ErrorCodes.ValidationFailed);
        await Check.ErrorAsync(async () => await ExecuteAsync("invalid"), ErrorCodes.ValidationFailed);
        await Check.ErrorAsync(async () => await ExecuteAsync(Guid.Empty.ToString()), ErrorCodes.ValidationFailed);
        await Check.ErrorAsync(async () => await ExecuteAsync(sbuId.ToString()), ErrorCodes.Forbidden);

        repository.Items.Add(UserSbu.Create(userId, sbuId, false));
        await Check.ErrorAsync(async () => await ExecuteAsync(sbuId.ToString()), ErrorCodes.Forbidden);
        sbu.Disable();
        repository.Sbus.Add(sbu);
        await Check.ErrorAsync(async () => await ExecuteAsync(sbuId.ToString()), ErrorCodes.Forbidden);
        sbu.Enable();
        await Check.ErrorAsync(async () => await ExecuteAsync(sbuId.ToString(), Guid.NewGuid()), ErrorCodes.Forbidden);
        var (called, selection) = await ExecuteAsync(sbuId.ToString());
        Check.That(called && selection.SbuId == sbuId && selection.SbuCode == "SBU2",
            "Only an enabled, assigned SBU may reach the action with its server-side code.");

        sbu.MarkDeleted();
        await Check.ErrorAsync(async () => await ExecuteAsync(sbuId.ToString()), ErrorCodes.Forbidden);
        Console.WriteLine("SBU request scope checks passed.");
    }
}
