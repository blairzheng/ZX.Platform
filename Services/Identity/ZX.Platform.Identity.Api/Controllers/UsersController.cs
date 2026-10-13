using Microsoft.AspNetCore.Mvc;
using ZX.Platform.Identity.Api.Contracts;
using ZX.Platform.Identity.Api.Localization;
using ZX.Platform.Identity.Application.Users;
using ZX.Platform.Identity.Application.Users.Dtos;

namespace ZX.Platform.Identity.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class UsersController : ControllerBase
    {
        private readonly IUserService _userService;
        private readonly IApiMessageLocalizer _messages;

        public UsersController(IUserService userService, IApiMessageLocalizer messages)
        {
            _userService = userService;
            _messages = messages;
        }

        [HttpPost("add")]
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
            await _userService.UpdateDisplayNameAsync(updateUserDisplayName, cancellationToken);
            var response = new ApiResponse
            {
                Code = 0,
                Message = _messages["User.DisplayNameUpdated"].Value
            };
            return StatusCode(StatusCodes.Status200OK, response);
        }

        [HttpPut("setUserEnable")]
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
        public async Task<IActionResult> SetUserDisableAsync([FromQuery] Guid id, CancellationToken cancellationToken)
        {
            await _userService.SetUserDisableAsync(id, cancellationToken);
            var response = new ApiResponse
            {
                Code = 0,
                Message = _messages["User.StatusUpdated"].Value
            };
            return StatusCode(StatusCodes.Status200OK, response);
        }

        [HttpPut("updatePassword")]
        public async Task<IActionResult> UpdatePasswordAsync([FromBody] UpdateUserPasswordDto updateUserPassword, CancellationToken cancellationToken)
        {
            await _userService.UpdatePasswordAsync(updateUserPassword, cancellationToken);
            var response = new ApiResponse
            {
                Code = 0,
                Message = _messages["User.PasswordUpdated"].Value
            };
            return StatusCode(StatusCodes.Status200OK, response);
        }

        [HttpPut("logicDelete")]
        public async Task<IActionResult> SoftDeleteAsync([FromQuery] Guid id, CancellationToken cancellationToken)
        {
            await _userService.SoftDeleteAsync(id, cancellationToken);
            var response = new ApiResponse
            {
                Code = 0,
                Message = _messages["User.Deleted"].Value
            };
            return StatusCode(StatusCodes.Status200OK, response);
        }

        [HttpPut("restore")]
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
        public async Task<IActionResult> BatchDeleteAsync([FromBody] List<Guid> guids, CancellationToken cancellationToken)
        {
            await _userService.BatchDeleteAsync(guids, cancellationToken);
            var response = new ApiResponse
            {
                Code = 0,
                Message = _messages["User.BatchDeleted"].Value
            };
            return StatusCode(StatusCodes.Status200OK, response);
        }
    }
}
