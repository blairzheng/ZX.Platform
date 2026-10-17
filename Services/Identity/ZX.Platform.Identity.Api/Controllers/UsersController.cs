using Microsoft.AspNetCore.Mvc;
using ZX.Platform.Identity.Api.Contracts;
using ZX.Platform.Identity.Api.Localization;
using ZX.Platform.Identity.Application.Users;
using ZX.Platform.Identity.Application.Users.Dtos;

using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using ZX.Platform.Identity.Api.Authentication;
using ZX.Platform.Identity.Application.Authorization;
using ZX.Platform.Identity.Application.Roles;

namespace ZX.Platform.Identity.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class UsersController : ControllerBase
    {
        private readonly IUserService _userService;
        private readonly IApiMessageLocalizer _messages;
        private readonly IPermissionService _permissions;
        private readonly IRoleService _roles;

        public UsersController(IUserService userService, IApiMessageLocalizer messages, IPermissionService permissions, IRoleService roles)
        {
            _userService = userService;
            _messages = messages;
            _permissions = permissions;
            _roles = roles;
        }

        [HttpPost("add")]
        [Authorize(Policy = IdentityPermissions.UserCreate)]
        public async Task<IActionResult> CreateAsync([FromBody] CreateUserDto createUser, CancellationToken cancellationToken)
        {
            var userId = await _userService.CreateAsync(createUser, cancellationToken);
            var response = new ApiResponse<Guid>
            {
                Code = 0,
                Message = _messages["User.Created"].Value,
                Data = userId
            };
            return StatusCode(StatusCodes.Status201Created, response);
        }

        [HttpGet("getUser")]
        [Authorize(Policy = IdentityPermissions.UserRead)]
        public async Task<IActionResult> GetByIdAsync([FromQuery] Guid id, CancellationToken cancellationToken)
        {
            var user = await _userService.GetByIdAsync(id, cancellationToken);
            var response = new ApiResponse<UserDetailDto>
            {
                Code = 0,
                Message = _messages["User.DetailsLoaded"].Value,
                Data = user
            };
            return StatusCode(StatusCodes.Status200OK, response);
        }

        [HttpGet("getUserList")]
        [Authorize(Policy = IdentityPermissions.UserRead)]
        public async Task<IActionResult> GetPageListAsync([FromQuery] UserQueryDto userQuery, CancellationToken cancellationToken)
        {
            var (users, count) = await _userService.GetPageListAsync(userQuery, cancellationToken);
            var response = new ApiResponse<UserPageDto>
            {
                Code = 0,
                Message = _messages["User.ListLoaded"].Value,
                Data = new UserPageDto
                {
                    List = users,
                    Total = count
                }
            };
            return StatusCode(StatusCodes.Status200OK, response);
        }

        [HttpPut("updateDisplayName")]
        public async Task<IActionResult> UpdateDisplayNameAsync([FromBody] UpdateUserDisplayNameDto updateUserDisplayName, CancellationToken cancellationToken)
        {
            if (updateUserDisplayName.Id != Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!) &&
                !await _permissions.HasAsync(CurrentUserId, IdentityPermissions.UserRename, cancellationToken))
                return Forbid();

            await _userService.UpdateDisplayNameAsync(updateUserDisplayName, cancellationToken);
            var response = new ApiResponse
            {
                Code = 0,
                Message = _messages["User.DisplayNameUpdated"].Value
            };
            return StatusCode(StatusCodes.Status200OK, response);
        }

        [HttpPut("setUserEnable")]
        [Authorize(Policy = IdentityPermissions.UserChangeStatus)]
        public async Task<IActionResult> SetUserEnableAsync([FromQuery] Guid id, CancellationToken cancellationToken)
        {
            await _userService.SetUserEnableAsync(id, cancellationToken);
            var response = new ApiResponse
            {
                Code = 0,
                Message = _messages["User.StatusUpdated"].Value
            };
            return StatusCode(StatusCodes.Status200OK, response);
        }

        [HttpPut("setUserDisable")]
        [Authorize(Policy = IdentityPermissions.UserChangeStatus)]
        public async Task<IActionResult> SetUserDisableAsync([FromQuery] Guid id, CancellationToken cancellationToken)
        {
            await _roles.EnsureCanDeactivateUsersAsync(CurrentUserId, [id], cancellationToken);
            await _userService.SetUserDisableAsync(id, cancellationToken);
            var response = new ApiResponse
            {
                Code = 0,
                Message = _messages["User.StatusUpdated"].Value
            };
            return StatusCode(StatusCodes.Status200OK, response);
        }

        [HttpPost("forceLogout")]
        [Authorize(Policy = IdentityPermissions.UserForceLogout)]
        public async Task<IActionResult> ForceLogoutAsync([FromQuery] Guid id, CancellationToken cancellationToken)
        {
            await _userService.ForceLogoutAsync(id, cancellationToken);
            var response = new ApiResponse
            {
                Code = 0,
                Message = _messages["Auth.ForcedLogout"].Value
            };
            return StatusCode(StatusCodes.Status200OK, response);
        }

        [HttpPut("logicDelete")]
        [Authorize(Policy = IdentityPermissions.UserDelete)]
        public async Task<IActionResult> SoftDeleteAsync([FromQuery] Guid id, CancellationToken cancellationToken)
        {
            await _roles.EnsureCanDeactivateUsersAsync(CurrentUserId, [id], cancellationToken);
            await _userService.SoftDeleteAsync(id, cancellationToken);
            var response = new ApiResponse
            {
                Code = 0,
                Message = _messages["User.Deleted"].Value
            };
            return StatusCode(StatusCodes.Status200OK, response);
        }

        [HttpPut("restore")]
        [Authorize(Policy = IdentityPermissions.UserRestore)]
        public async Task<IActionResult> RestoreAsync([FromQuery] Guid id, CancellationToken cancellationToken)
        {
            await _userService.RestoreAsync(id, cancellationToken);
            var response = new ApiResponse
            {
                Code = 0,
                Message = _messages["User.Restored"].Value
            };
            return StatusCode(StatusCodes.Status200OK, response);
        }

        [HttpPut("updateUserFactories")]
        [Authorize(Policy = IdentityPermissions.UserAssignFactories)]
        public async Task<IActionResult> UpdateUserFactoriesAsync([FromBody] UpdateUserFactoryDto updateUserFactory, CancellationToken cancellationToken)
        {
            await _userService.UpdateUserFactoriesAsync(updateUserFactory, cancellationToken);
            var response = new ApiResponse
            {
                Code = 0,
                Message = _messages["User.FactoriesUpdated"].Value
            };
            return StatusCode(StatusCodes.Status200OK, response);
        }

        [HttpPut("batchDelete")]
        [Authorize(Policy = IdentityPermissions.UserDelete)]
        public async Task<IActionResult> BatchDeleteAsync([FromBody] List<Guid> guids, CancellationToken cancellationToken)
        {
            await _roles.EnsureCanDeactivateUsersAsync(CurrentUserId, guids, cancellationToken);
            await _userService.BatchDeleteAsync(guids, cancellationToken);
            var response = new ApiResponse
            {
                Code = 0,
                Message = _messages["User.BatchDeleted"].Value
            };
            return StatusCode(StatusCodes.Status200OK, response);
        }
        private Guid CurrentUserId => Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
    }
}
