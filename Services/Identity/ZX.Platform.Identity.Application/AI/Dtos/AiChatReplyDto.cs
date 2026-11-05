namespace ZX.Platform.Identity.Application.AI.Dtos;

public sealed class AiChatReplyDto
{
    public required string Reply { get; init; }
    public required string Model { get; init; }
    public bool IsTruncated { get; init; }
    public IReadOnlyList<AiSourceDto> Sources { get; init; } = [];
}

public sealed record AiSourceDto(string Path, int StartLine);
