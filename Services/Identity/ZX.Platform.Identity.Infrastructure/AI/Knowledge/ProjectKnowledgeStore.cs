using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ZX.Platform.Identity.Application.AI.Knowledge;

namespace ZX.Platform.Identity.Infrastructure.AI.Knowledge;

// Runtime reads only the exported catalog, never paths supplied by a model.
public sealed class ProjectKnowledgeStore(string catalogPath) : IProjectKnowledge
{
    private const int MaxCatalogBytes = 12 * 1024 * 1024;
    private readonly Lazy<Task<SourceCatalog?>> _catalog = new(() => LoadAsync(catalogPath));

    public async Task<ProjectKnowledgeSearchResult> SearchAsync(string query, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Length > 300)
            return new ProjectKnowledgeSearchResult { Status = "invalid_request" };

        var catalog = await _catalog.Value.WaitAsync(cancellationToken);
        if (catalog is null) return new ProjectKnowledgeSearchResult();

        var terms = GetTerms(query);
        if (terms.Count == 0)
            return new ProjectKnowledgeSearchResult { Status = "invalid_request", ExportedAtUtc = catalog.ExportedAtUtc };

        var matches = new List<(SourceDocument Document, int Line, double Score)>();
        foreach (var document in catalog.Documents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var pathScore = Score(document.Path, query, terms) * 2;
            var bestLine = 0;
            var bestScore = 0d;
            for (var line = 0; line < document.Lines.Length; line++)
            {
                var score = Score(document.Lines[line], query, terms);
                if (score <= bestScore) continue;
                bestLine = line;
                bestScore = score;
            }
            if (pathScore + bestScore > 0)
                matches.Add((document, bestLine, pathScore + bestScore));
        }

        return new ProjectKnowledgeSearchResult
        {
            Status = "ok",
            ExportedAtUtc = catalog.ExportedAtUtc,
            Matches = matches.OrderByDescending(match => match.Score)
                .ThenBy(match => match.Document.Path, StringComparer.Ordinal)
                .Take(6)
                .Select(match => Excerpt(match.Document, Math.Max(1, match.Line - 3), 24, 1500))
                .ToArray()
        };
    }

    public async Task<ProjectKnowledgeReadResult> ReadAsync(string path, int startLine, int lineCount,
        CancellationToken cancellationToken)
    {
        if (!IsCatalogPath(path) || startLine < 1 || lineCount < 1 || lineCount > 120)
            return new ProjectKnowledgeReadResult { Status = "invalid_request" };

        var catalog = await _catalog.Value.WaitAsync(cancellationToken);
        if (catalog is null) return new ProjectKnowledgeReadResult();

        var document = catalog.Documents.FirstOrDefault(source => source.Path == path);
        if (document is null || startLine > document.Lines.Length)
            return new ProjectKnowledgeReadResult { Status = "not_found", ExportedAtUtc = catalog.ExportedAtUtc };

        cancellationToken.ThrowIfCancellationRequested();
        return new ProjectKnowledgeReadResult
        {
            Status = "ok",
            ExportedAtUtc = catalog.ExportedAtUtc,
            Source = Excerpt(document, startLine, lineCount, 16000)
        };
    }

    private static ProjectSourceExcerpt Excerpt(SourceDocument document, int startLine, int lineCount, int maxCharacters)
    {
        var content = new StringBuilder();
        var endLine = startLine;
        var lastLine = Math.Min(document.Lines.Length, startLine - 1 + lineCount);
        var truncated = false;
        for (var index = startLine - 1; index < lastLine; index++)
        {
            var line = document.Lines[index];
            var separator = content.Length == 0 ? string.Empty : "\n";
            var remaining = maxCharacters - content.Length - separator.Length;
            if (remaining < 0)
            {
                truncated = true;
                break;
            }
            content.Append(separator);
            content.Append(line.AsSpan(0, Math.Min(line.Length, remaining)));
            endLine = index + 1;
            if (line.Length > remaining)
            {
                truncated = true;
                break;
            }
        }
        return new ProjectSourceExcerpt
        {
            Path = document.Path,
            StartLine = startLine,
            EndLine = endLine,
            TotalLines = document.Lines.Length,
            Content = content.ToString(),
            Sha256 = document.Sha256,
            Truncated = truncated
        };
    }

    private static IReadOnlyList<string> GetTerms(string query)
    {
        var tokens = Regex.Matches(query, @"[A-Za-z0-9_]+|[\u4e00-\u9fff]+", RegexOptions.CultureInvariant);
        var terms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match token in tokens)
        {
            if (token.Value[0] <= 127)
            {
                terms.Add(token.Value);
                foreach (var word in Regex.Split(token.Value, @"(?<=[a-z])(?=[A-Z])|_", RegexOptions.CultureInvariant))
                    if (word.Length > 1) terms.Add(word);
            }
            else
            {
                if (token.Length <= 8) terms.Add(token.Value);
                for (var index = 0; index < token.Length - 1; index++)
                    terms.Add(token.Value.Substring(index, 2));
            }
        }
        return terms.Take(40).ToArray();
    }

    private static double Score(string text, string query, IReadOnlyList<string> terms)
    {
        var score = text.Contains(query, StringComparison.OrdinalIgnoreCase) ? 8d : 0;
        foreach (var term in terms)
            if (text.Contains(term, StringComparison.OrdinalIgnoreCase)) score += Math.Min(term.Length, 10);
        return score;
    }

    private static bool IsCatalogPath(string? path)
        => !string.IsNullOrWhiteSpace(path) && path.Length <= 400 && !path.Contains('\\') &&
            !path.Contains(':') && !path.Any(char.IsControl) &&
            (path.StartsWith("frontend/src/", StringComparison.Ordinal) ||
                path.StartsWith("backend/Services/Identity/", StringComparison.Ordinal)) &&
            path.Split('/').All(part => part.Length > 0 && part is not ("." or ".."));

    private static async Task<SourceCatalog?> LoadAsync(string path)
    {
        try
        {
            var file = new FileInfo(path);
            if (!file.Exists || file.Length is <= 0 or > MaxCatalogBytes || file.LinkTarget is not null) return null;
            await using var stream = File.OpenRead(path);
            var catalog = await JsonSerializer.DeserializeAsync<SourceCatalog>(stream,
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            if (catalog?.SchemaVersion != 1 || catalog.ExportedAtUtc == default ||
                catalog.Documents is not { Count: > 0 and <= 2000 }) return null;

            var paths = new HashSet<string>(StringComparer.Ordinal);
            foreach (var document in catalog.Documents)
            {
                if (document is null || !IsCatalogPath(document.Path) || !paths.Add(document.Path) || document.Text is null ||
                    document.Text.Length > 512 * 1024 || document.Text.Contains('\0') || document.Sha256 is null)
                    return null;
                var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(document.Text)));
                if (!string.Equals(hash, document.Sha256, StringComparison.Ordinal)) return null;
                document.Lines = document.Text.Split('\n');
            }
            return catalog;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or
            ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    private sealed class SourceCatalog
    {
        public SourceCatalog() { }

        public int SchemaVersion { get; set; }
        public DateTimeOffset ExportedAtUtc { get; set; }
        public List<SourceDocument> Documents { get; set; } = [];
    }

    private sealed class SourceDocument
    {
        public SourceDocument() { }

        public string Path { get; set; } = string.Empty;
        public string Sha256 { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
        public string[] Lines { get; set; } = [];
    }
}
