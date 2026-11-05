using System.ComponentModel.DataAnnotations;

namespace ZX.Platform.Identity.Infrastructure.AI;

public sealed class AssistantOptions
{
    public string BaseUrl { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;

    [Range(5, 60)]
    public int TimeoutSeconds { get; set; } = 60;

    [Range(256, 4096)]
    public int MaxTokens { get; set; } = 2048;

    [Range(2, 6)]
    public int MaxModelRounds { get; set; } = 5;

    [Range(4096, 131072)]
    public int MaxToolResultBytes { get; set; } = 65536;
}
