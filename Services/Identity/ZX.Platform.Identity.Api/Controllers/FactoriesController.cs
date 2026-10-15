using Microsoft.AspNetCore.Mvc;
using ZX.Platform.Identity.Api.Contracts;
using ZX.Platform.Identity.Api.Localization;
using ZX.Platform.Identity.Application.Factories;
using ZX.Platform.Identity.Application.Factories.Dtos;

using Microsoft.AspNetCore.Authorization;
using ZX.Platform.Identity.Api.Authentication;

namespace ZX.Platform.Identity.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(Policy = JwtSessionEvents.AdministratorPolicy)]
    public class FactoriesController : ControllerBase
    {
        private readonly IFactoryService _factoryService;
        private readonly IApiMessageLocalizer _messages;

        public FactoriesController(IFactoryService factoryService, IApiMessageLocalizer messages)
        {
            _factoryService = factoryService;
            _messages = messages;
        }

        [HttpPost("add")]
        public async Task<IActionResult> CreateAsync([FromBody] CreateFactoryDto createFactory, CancellationToken cancellationToken)
        {
            var factoryId = await _factoryService.CreateAsync(createFactory, cancellationToken);
            var response = new ApiResponse<Guid>
            {
                Code = 0,
                Message = _messages["Factory.Created"].Value,
                Data = factoryId
            };
            return StatusCode(StatusCodes.Status201Created, response);
        }

        [HttpGet("getFactory")]
        public async Task<IActionResult> GetByIdAsync([FromQuery] Guid id, CancellationToken cancellationToken)
        {
            var factory = await _factoryService.GetByIdAsync(id, cancellationToken);
            var response = new ApiResponse<FactoryDetailDto>
            {
                Code = 0,
                Message = _messages["Factory.DetailsLoaded"].Value,
                Data = factory
            };
            return StatusCode(StatusCodes.Status200OK, response);
        }

        [HttpGet("getFactoryList")]
        public async Task<IActionResult> GetAllAsync(CancellationToken cancellationToken)
        {
            var factories = await _factoryService.GetAllAsync(cancellationToken);
            var response = new ApiResponse<List<FactoryDetailDto>>
            {
                Code = 0,
                Message = _messages["Factory.ListLoaded"].Value,
                Data = factories
            };
            return StatusCode(StatusCodes.Status200OK, response);
        }

        [HttpPut("updateName")]
        public async Task<IActionResult> UpdateNameAsync([FromBody] UpdateFactoryNameDto updateFactoryName, CancellationToken cancellationToken)
        {
            await _factoryService.UpdateNameAsync(updateFactoryName, cancellationToken);
            var response = new ApiResponse
            {
                Code = 0,
                Message = _messages["Factory.NameUpdated"].Value
            };
            return StatusCode(StatusCodes.Status200OK, response);
        }

        [HttpPut("setFactoryEnable")]
        public async Task<IActionResult> SetFactoryEnableAsync([FromQuery] Guid id, CancellationToken cancellationToken)
        {
            await _factoryService.SetFactoryEnableAsync(id, cancellationToken);
            var response = new ApiResponse
            {
                Code = 0,
                Message = _messages["Factory.StatusUpdated"].Value
            };
            return StatusCode(StatusCodes.Status200OK, response);
        }

        [HttpPut("setFactoryDisable")]
        public async Task<IActionResult> SetFactoryDisableAsync([FromQuery] Guid id, CancellationToken cancellationToken)
        {
            await _factoryService.SetFactoryDisableAsync(id, cancellationToken);
            var response = new ApiResponse
            {
                Code = 0,
                Message = _messages["Factory.StatusUpdated"].Value
            };
            return StatusCode(StatusCodes.Status200OK, response);
        }

        [HttpPut("logicDelete")]
        public async Task<IActionResult> SoftDeleteAsync([FromQuery] Guid id, CancellationToken cancellationToken)
        {
            await _factoryService.SoftDeleteAsync(id, cancellationToken);
            var response = new ApiResponse
            {
                Code = 0,
                Message = _messages["Factory.Deleted"].Value
            };
            return StatusCode(StatusCodes.Status200OK, response);
        }

        [HttpPut("restore")]
        public async Task<IActionResult> RestoreAsync([FromQuery] Guid id, CancellationToken cancellationToken)
        {
            await _factoryService.RestoreAsync(id, cancellationToken);
            var response = new ApiResponse
            {
                Code = 0,
                Message = _messages["Factory.Restored"].Value
            };
            return StatusCode(StatusCodes.Status200OK, response);
        }

        [HttpGet("getAllEnabledFactories")]
        public async Task<IActionResult> GetAllEnabledFactoriesAsync(CancellationToken cancellationToken)
        {
            var factories = await _factoryService.GetAllEnabledFactoriesAsync(cancellationToken);
            var response = new ApiResponse<List<FactoryDetailDto>>
            {
                Code = 0,
                Message = _messages["Factory.EnabledListLoaded"].Value,
                Data = factories
            };
            return StatusCode(StatusCodes.Status200OK, response);
        }
    }
}
