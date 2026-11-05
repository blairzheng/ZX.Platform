namespace ZX.Platform.Identity.Application.AI;

public interface IAssistantModel
{
    Task<AssistantCompletion> CompleteAsync(IReadOnlyList<AssistantMessage> messages, bool allowTools,
        CancellationToken cancellationToken);
}

// 此处的 system、tool 和工具调用只由服务端构造，不暴露为浏览器输入。
public sealed record AssistantMessage(string Role, string? Content,
    IReadOnlyList<AssistantToolCall>? ToolCalls = null, string? ToolCallId = null, string? ReasoningContent = null);

public sealed record AssistantToolCall(string Id, string Name, string Arguments);

public sealed record AssistantCompletion(string Model, string? Content,
    IReadOnlyList<AssistantToolCall> ToolCalls, bool IsTruncated, string? ReasoningContent = null);
