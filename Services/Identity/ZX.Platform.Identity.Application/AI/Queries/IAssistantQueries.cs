using System.Text.Json;

namespace ZX.Platform.Identity.Application.AI.Queries;

// The API implementation discovers explicitly marked read-only endpoints and
// executes them as the actual caller. A model cannot supply a user, URL or method.
public interface IAssistantQueries
{
    Task<AssistantToolResult> ListAsync(string? query, int offset, CancellationToken cancellationToken);

    Task<AssistantToolResult> ExecuteAsync(string id, JsonElement parameters, JsonElement? filters,
        CancellationToken cancellationToken);
}
