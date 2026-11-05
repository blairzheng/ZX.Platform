using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ZX.Platform.Identity.Api.AI.Queries;
using ZX.Platform.Identity.Api.Contracts;
using ZX.Platform.Identity.Api.Localization;
using ZX.Platform.Identity.Application.Authorization;
using ZX.Platform.Identity.Application.Roles;
using ZX.Platform.Identity.Application.Roles.Dtos;
using ZX.Platform.Identity.Application.Menus;
using ZX.Platform.Identity.Application.Menus.Dtos;

namespace ZX.Platform.Identity.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public sealed class RolesController(IRoleService roles, IMenuService menus, IApiMessageLocalizer messages) : ControllerBase
{
    [AiQuery(ResultShape = AiQueryResultShape.CompleteList)]
    [HttpGet("getRoleList")]
    [Authorize(Policy = IdentityPermissions.RoleRead)]
    [ProducesResponseType(typeof(ApiResponse<List<RoleDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAllAsync(CancellationToken cancellationToken) => Ok(new ApiResponse<List<RoleDto>>
    {
        Code = 0, Message = messages["Access.RoleListLoaded"].Value, Data = await roles.GetAllAsync(cancellationToken)
    });

    // 用户分配角色只需要分配权限，不要求具备角色管理页面的读取权限。
    [AiQuery(ResultShape = AiQueryResultShape.CompleteList)]
    [HttpGet("options")]
    [Authorize(Policy = IdentityPermissions.UserAssignRoles)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetOptionsAsync(CancellationToken cancellationToken)
    {
        var list = await roles.GetAllAsync(cancellationToken);
        return Ok(new ApiResponse<object>
        {
            Code = 0, Message = messages["Access.RoleListLoaded"].Value,
            Data = list.Select(role => new { role.Id, role.Code, role.Name, role.IsEnabled, role.IsBuiltIn }).ToArray()
        });
    }

    [HttpPost("add")]
    [Authorize(Policy = IdentityPermissions.RoleCreate)]
    public async Task<IActionResult> CreateAsync(CreateRoleDto input, CancellationToken cancellationToken) =>
        StatusCode(StatusCodes.Status201Created, new ApiResponse<Guid>
        {
            Code = 0, Message = messages["Access.RoleCreated"].Value, Data = await roles.CreateAsync(input, cancellationToken)
        });

    [HttpPut("update")]
    [Authorize(Policy = IdentityPermissions.RoleUpdate)]
    public async Task<IActionResult> UpdateAsync(UpdateRoleDto input, CancellationToken cancellationToken)
    {
        await roles.UpdateAsync(input, cancellationToken);
        return Ok(new ApiResponse { Code = 0, Message = messages["Access.RoleUpdated"].Value });
    }

    [HttpPut("setRoleEnable")]
    [Authorize(Policy = IdentityPermissions.RoleChangeStatus)]
    public async Task<IActionResult> EnableAsync([FromQuery] Guid id, CancellationToken cancellationToken)
    {
        await roles.SetEnabledAsync(id, true, cancellationToken);
        return Ok(new ApiResponse { Code = 0, Message = messages["Access.RoleStatusUpdated"].Value });
    }

    [HttpPut("setRoleDisable")]
    [Authorize(Policy = IdentityPermissions.RoleChangeStatus)]
    public async Task<IActionResult> DisableAsync([FromQuery] Guid id, CancellationToken cancellationToken)
    {
        await roles.SetEnabledAsync(id, false, cancellationToken);
        return Ok(new ApiResponse { Code = 0, Message = messages["Access.RoleStatusUpdated"].Value });
    }

    [HttpPut("logicDelete")]
    [Authorize(Policy = IdentityPermissions.RoleDelete)]
    public async Task<IActionResult> DeleteAsync([FromQuery] Guid id, CancellationToken cancellationToken)
    {
        await roles.DeleteAsync(id, cancellationToken);
        return Ok(new ApiResponse { Code = 0, Message = messages["Access.RoleDeleted"].Value });
    }

    [AiQuery(ResultShape = AiQueryResultShape.CompleteList)]
    [HttpGet("permissions")]
    [Authorize(Policy = IdentityPermissions.RoleAssignPermissions)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public IActionResult GetPermissions() => Ok(new ApiResponse<object>
    {
        Code = 0, Message = messages["Access.PermissionListLoaded"].Value,
        Data = IdentityPermissions.All.Select(code => new
        {
            Code = code, Group = code.Split('.')[1],
            Name = messages[$"Access.Permission.{code}"].Value,
            GroupName = messages[$"Access.Group.{code.Split('.')[1]}"].Value
        }).ToArray()
    });

    [AiQuery(ResultShape = AiQueryResultShape.CompleteTree)]
    [HttpGet("permissionTree")]
    [Authorize(Policy = IdentityPermissions.RoleAssignPermissions)]
    [ProducesResponseType(typeof(ApiResponse<List<MenuDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPermissionTreeAsync(CancellationToken cancellationToken) => Ok(new ApiResponse<object>
    { Code = 0, Message = messages["Access.PermissionListLoaded"].Value, Data = await menus.GetPermissionTreeAsync(cancellationToken) });

    [HttpPut("updatePermissions")]
    [Authorize(Policy = IdentityPermissions.RoleAssignPermissions)]
    public async Task<IActionResult> UpdatePermissionsAsync(UpdateRolePermissionsDto input, CancellationToken cancellationToken)
    {
        await roles.UpdatePermissionsAsync(input, cancellationToken);
        return Ok(new ApiResponse { Code = 0, Message = messages["Access.PermissionsUpdated"].Value });
    }

    [AiQuery(ResultShape = AiQueryResultShape.CompleteList)]
    [HttpGet("userRoles")]
    [Authorize(Policy = IdentityPermissions.UserAssignRoles)]
    [ProducesResponseType(typeof(ApiResponse<List<Guid>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetUserRolesAsync([FromQuery] Guid userId, CancellationToken cancellationToken) => Ok(new ApiResponse<List<Guid>>
    {
        Code = 0, Message = messages["Access.UserRolesLoaded"].Value, Data = await roles.GetUserRolesAsync(userId, cancellationToken)
    });

    [HttpPut("updateUserRoles")]
    [Authorize(Policy = IdentityPermissions.UserAssignRoles)]
    public async Task<IActionResult> UpdateUserRolesAsync(UpdateUserRolesDto input, CancellationToken cancellationToken)
    {
        await roles.UpdateUserRolesAsync(Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!), input, cancellationToken);
        return Ok(new ApiResponse { Code = 0, Message = messages["Access.UserRolesUpdated"].Value });
    }
}
