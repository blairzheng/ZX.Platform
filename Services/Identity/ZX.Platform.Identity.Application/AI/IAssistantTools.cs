using ZX.Platform.Identity.Application.AI.Dtos;

namespace ZX.Platform.Identity.Application.AI;

public interface IAssistantTools
{
    Task<AssistantToolResult> ExecuteAsync(AssistantToolCall call, Guid userId,
        CancellationToken cancellationToken);
}

public sealed record AssistantToolResult(string Content, IReadOnlyList<AiSourceDto> Sources);

public sealed class AssistantLimits
{
    public int MaxModelRounds { get; init; } = 5;
    public int MaxToolResultBytes { get; init; } = 64 * 1024;
}
