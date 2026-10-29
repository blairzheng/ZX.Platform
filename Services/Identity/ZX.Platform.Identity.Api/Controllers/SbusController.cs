using Microsoft.AspNetCore.Mvc;
using ZX.Platform.Identity.Api.Contracts;
using ZX.Platform.Identity.Api.Localization;
using ZX.Platform.Identity.Application.Sbus;
using ZX.Platform.Identity.Application.Sbus.Dtos;

using Microsoft.AspNetCore.Authorization;
using ZX.Platform.Identity.Api.Authorization;
using ZX.Platform.Identity.Application.Authorization;

namespace ZX.Platform.Identity.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class SbusController : ControllerBase
    {
        private readonly ISbuService _sbuService;
        private readonly IApiMessageLocalizer _messages;

        public SbusController(ISbuService sbuService, IApiMessageLocalizer messages)
        {
            _sbuService = sbuService;
            _messages = messages;
        }

        [HttpPost("add")]
        [Authorize(Policy = IdentityPermissions.SbuCreate)]
        public async Task<IActionResult> CreateAsync([FromBody] CreateSbuDto createSbu, CancellationToken cancellationToken)
        {
            var sbuId = await _sbuService.CreateAsync(createSbu, cancellationToken);
            var response = new ApiResponse<Guid>
            {
                Code = 0,
                Message = _messages["Sbu.Created"].Value,
                Data = sbuId
            };
            return StatusCode(StatusCodes.Status201Created, response);
        }

        [HttpGet("getSbu")]
        [Authorize(Policy = IdentityPermissions.SbuRead)]
        public async Task<IActionResult> GetByIdAsync([FromQuery] Guid id, CancellationToken cancellationToken)
        {
            var sbu = await _sbuService.GetByIdAsync(id, cancellationToken);
            var response = new ApiResponse<SbuDetailDto>
            {
                Code = 0,
                Message = _messages["Sbu.DetailsLoaded"].Value,
                Data = sbu
            };
            return StatusCode(StatusCodes.Status200OK, response);
        }

        [HttpGet("getSbuList")]
        [Authorize(Policy = IdentityPermissions.SbuRead)]
        public async Task<IActionResult> GetAllAsync(CancellationToken cancellationToken)
        {
            var sbus = await _sbuService.GetAllAsync(cancellationToken);
            var response = new ApiResponse<List<SbuDetailDto>>
            {
                Code = 0,
                Message = _messages["Sbu.ListLoaded"].Value,
                Data = sbus
            };
            return StatusCode(StatusCodes.Status200OK, response);
        }

        [HttpPut("updateName")]
        [Authorize(Policy = IdentityPermissions.SbuRename)]
        public async Task<IActionResult> UpdateNameAsync([FromBody] UpdateSbuNameDto updateSbuName, CancellationToken cancellationToken)
        {
            await _sbuService.UpdateNameAsync(updateSbuName, cancellationToken);
            var response = new ApiResponse
            {
                Code = 0,
                Message = _messages["Sbu.NameUpdated"].Value
            };
            return StatusCode(StatusCodes.Status200OK, response);
        }

        [HttpPut("setSbuEnable")]
        [Authorize(Policy = IdentityPermissions.SbuChangeStatus)]
        public async Task<IActionResult> SetSbuEnableAsync([FromQuery] Guid id, CancellationToken cancellationToken)
        {
            await _sbuService.SetSbuEnableAsync(id, cancellationToken);
            var response = new ApiResponse
            {
                Code = 0,
                Message = _messages["Sbu.StatusUpdated"].Value
            };
            return StatusCode(StatusCodes.Status200OK, response);
        }

        [HttpPut("setSbuDisable")]
        [Authorize(Policy = IdentityPermissions.SbuChangeStatus)]
        public async Task<IActionResult> SetSbuDisableAsync([FromQuery] Guid id, CancellationToken cancellationToken)
        {
            await _sbuService.SetSbuDisableAsync(id, cancellationToken);
            var response = new ApiResponse
            {
                Code = 0,
                Message = _messages["Sbu.StatusUpdated"].Value
            };
            return StatusCode(StatusCodes.Status200OK, response);
        }

        [HttpPut("logicDelete")]
        [Authorize(Policy = IdentityPermissions.SbuDelete)]
        public async Task<IActionResult> SoftDeleteAsync([FromQuery] Guid id, CancellationToken cancellationToken)
        {
            await _sbuService.SoftDeleteAsync(id, cancellationToken);
            var response = new ApiResponse
            {
                Code = 0,
                Message = _messages["Sbu.Deleted"].Value
            };
            return StatusCode(StatusCodes.Status200OK, response);
        }

        [HttpPut("restore")]
        [Authorize(Policy = IdentityPermissions.SbuRestore)]
        public async Task<IActionResult> RestoreAsync([FromQuery] Guid id, CancellationToken cancellationToken)
        {
            await _sbuService.RestoreAsync(id, cancellationToken);
            var response = new ApiResponse
            {
                Code = 0,
                Message = _messages["Sbu.Restored"].Value
            };
            return StatusCode(StatusCodes.Status200OK, response);
        }

        [HttpGet("getAllEnabledSbus")]
        [Authorize(Policy = PermissionPolicies.SbuOptions)]
        public async Task<IActionResult> GetAllEnabledSbusAsync(CancellationToken cancellationToken)
        {
            var sbus = await _sbuService.GetAllEnabledSbusAsync(cancellationToken);
            var response = new ApiResponse<List<SbuDetailDto>>
            {
                Code = 0,
                Message = _messages["Sbu.EnabledListLoaded"].Value,
                Data = sbus
            };
            return StatusCode(StatusCodes.Status200OK, response);
        }
    }
}
