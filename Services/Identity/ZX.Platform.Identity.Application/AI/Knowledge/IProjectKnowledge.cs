namespace ZX.Platform.Identity.Application.AI.Knowledge;

public interface IProjectKnowledge
{
    Task<ProjectKnowledgeSearchResult> SearchAsync(string query, CancellationToken cancellationToken);
    Task<ProjectKnowledgeReadResult> ReadAsync(string path, int startLine, int lineCount,
        CancellationToken cancellationToken);
}

public sealed class ProjectKnowledgeSearchResult
{
    public string Status { get; init; } = "unavailable";
    public DateTimeOffset? ExportedAtUtc { get; init; }
    public IReadOnlyList<ProjectSourceExcerpt> Matches { get; init; } = [];
}

public sealed class ProjectKnowledgeReadResult
{
    public string Status { get; init; } = "unavailable";
    public DateTimeOffset? ExportedAtUtc { get; init; }
    public ProjectSourceExcerpt? Source { get; init; }
}

public sealed class ProjectSourceExcerpt
{
    public string Path { get; init; } = string.Empty;
    public int StartLine { get; init; }
    public int EndLine { get; init; }
    public int TotalLines { get; init; }
    public string Content { get; init; } = string.Empty;
    public string Sha256 { get; init; } = string.Empty;
    public bool Truncated { get; init; }
}
