using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ZX.Platform.Identity.Api.Authorization;
using ZX.Platform.Identity.Api.Contracts;
using ZX.Platform.Identity.Api.Localization;
using ZX.Platform.Identity.Application.Authorization;
using ZX.Platform.Identity.Application.Menus;
using ZX.Platform.Identity.Application.Menus.Dtos;

namespace ZX.Platform.Identity.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public sealed class MenusController(IMenuService menus, IApiMessageLocalizer messages) : ControllerBase
{
    [HttpGet("getMenuTree")]
    [Authorize(Policy = IdentityPermissions.MenuRead)]
    public async Task<IActionResult> GetTreeAsync(CancellationToken cancellationToken) => Ok(new ApiResponse<List<MenuDto>>
    { Code = 0, Message = messages["Access.MenuTreeLoaded"].Value, Data = await menus.GetTreeAsync(cancellationToken) });

    [HttpGet("options")]
    [Authorize(Policy = PermissionPolicies.MenuOptions)]
    public async Task<IActionResult> GetOptionsAsync(CancellationToken cancellationToken) => Ok(new ApiResponse<List<MenuDto>>
    { Code = 0, Message = messages["Access.MenuTreeLoaded"].Value, Data = await menus.GetTreeAsync(cancellationToken) });

    [HttpGet("permissions")]
    [Authorize(Policy = PermissionPolicies.MenuOptions)]
    public IActionResult GetPermissions() => Ok(new ApiResponse<object>
    {
        Code = 0, Message = messages["Access.PermissionListLoaded"].Value,
        Data = IdentityPermissions.All.Select(code => new { Code = code, Name = messages[$"Access.Permission.{code}"].Value }).ToArray()
    });

    [HttpPost("add")]
    [Authorize(Policy = IdentityPermissions.MenuCreate)]
    public async Task<IActionResult> CreateAsync(CreateMenuDto input, CancellationToken cancellationToken) =>
        StatusCode(StatusCodes.Status201Created, new ApiResponse<Guid>
        { Code = 0, Message = messages["Access.MenuCreated"].Value, Data = await menus.CreateAsync(input, cancellationToken) });

    [HttpPut("update")]
    [Authorize(Policy = IdentityPermissions.MenuUpdate)]
    public async Task<IActionResult> UpdateAsync(UpdateMenuDto input, CancellationToken cancellationToken)
    {
        await menus.UpdateAsync(input, cancellationToken);
        return Ok(new ApiResponse { Code = 0, Message = messages["Access.MenuUpdated"].Value });
    }

    [HttpPut("setEnabled")]
    [Authorize(Policy = IdentityPermissions.MenuChangeStatus)]
    public async Task<IActionResult> SetEnabledAsync([FromQuery] Guid id, [FromQuery] bool enabled, CancellationToken cancellationToken)
    {
        await menus.SetEnabledAsync(id, enabled, cancellationToken);
        return Ok(new ApiResponse { Code = 0, Message = messages["Access.MenuStatusUpdated"].Value });
    }

    [HttpPut("logicDelete")]
    [Authorize(Policy = IdentityPermissions.MenuDelete)]
    public async Task<IActionResult> DeleteAsync([FromQuery] Guid id, CancellationToken cancellationToken)
    {
        await menus.DeleteAsync(id, cancellationToken);
        return Ok(new ApiResponse { Code = 0, Message = messages["Access.MenuDeleted"].Value });
    }
}
