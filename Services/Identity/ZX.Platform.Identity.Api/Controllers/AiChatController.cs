using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ZX.Platform.Identity.Api.Contracts;
using ZX.Platform.Identity.Api.Localization;
using ZX.Platform.Identity.Application.AI;
using ZX.Platform.Identity.Application.AI.Dtos;
using ZX.Platform.Identity.Application.Authorization;
using ZX.Platform.Identity.Application.Errors;
using ZX.Platform.Identity.Application.Exceptions;
using ZX.Platform.Identity.Infrastructure.AI;

namespace ZX.Platform.Identity.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = IdentityPermissions.AiChatUse)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AiChatController(IAssistantService assistant, AssistantOptions options,
    IApiMessageLocalizer messages) : ControllerBase
{
    [HttpPost("reply")]
    [RequestSizeLimit(256 * 1024)]
    public async Task<IActionResult> ReplyAsync([FromBody] AiChatRequestDto input, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(JwtRegisteredClaimNames.Sub), out var userId)) return Unauthorized();
        // 多轮模型和工具共用一个总时限，浏览器取消时中止整条调用链。
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));
        try
        {
            var reply = await assistant.ReplyAsync(input, userId, timeout.Token);
            return Ok(new ApiResponse<AiChatReplyDto>
            {
                Code = 0,
                Message = messages["AI.Replied"].Value,
                Data = reply
            });
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new BusinessException(ErrorCodes.ServiceUnavailable, "AI.Timeout");
        }
    }
}
