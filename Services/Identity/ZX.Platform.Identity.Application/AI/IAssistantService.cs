using ZX.Platform.Identity.Application.AI.Dtos;

namespace ZX.Platform.Identity.Application.AI;

public interface IAssistantService
{
    Task<AiChatReplyDto> ReplyAsync(AiChatRequestDto input, Guid userId, CancellationToken cancellationToken);
}
