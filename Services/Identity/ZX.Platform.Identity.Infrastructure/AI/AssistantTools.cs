using System.Text.Encodings.Web;
using System.Text.Json;
using ZX.Platform.Identity.Application.AI;
using ZX.Platform.Identity.Application.AI.Dtos;
using ZX.Platform.Identity.Application.AI.Knowledge;
using ZX.Platform.Identity.Application.AI.Queries;
using ZX.Platform.Identity.Application.Authorization;

namespace ZX.Platform.Identity.Infrastructure.AI;

public sealed class AssistantTools(IProjectKnowledge knowledge, IAssistantQueries queries,
    IPermissionService permissions, AssistantLimits limits) : IAssistantTools
{
    // 工具内容只作为 JSON 字符串交给模型，不作为 HTML 输出，避免中文转义使预算膨胀。
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public async Task<AssistantToolResult> ExecuteAsync(AssistantToolCall call, Guid userId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using var document = JsonDocument.Parse(call.Arguments, new JsonDocumentOptions { MaxDepth = 8 });
            var arguments = document.RootElement;
            if (arguments.ValueKind != JsonValueKind.Object) return Status("invalid_arguments");
            switch (call.Name)
            {
                case "search_project_sources":
                {
                    if (!OnlyProperties(arguments, "query")) return Status("invalid_arguments");
                    var query = GetText(arguments, "query", 200);
                    if (string.IsNullOrWhiteSpace(query)) return Status("invalid_arguments");
                    var result = await knowledge.SearchAsync(query, cancellationToken);
                    return Result(result, result.Matches.Select(source => new AiSourceDto(source.Path, source.StartLine)).ToArray());
                }
                case "read_project_source":
                {
                    if (!OnlyProperties(arguments, "path", "startLine", "lineCount")) return Status("invalid_arguments");
                    var path = GetText(arguments, "path", 400);
                    if (string.IsNullOrWhiteSpace(path) || !GetInt(arguments, "startLine", out var startLine) ||
                        !GetInt(arguments, "lineCount", out var lineCount) || startLine < 1 || lineCount is < 1 or > 120)
                        return Status("invalid_arguments");
                    var result = await knowledge.ReadAsync(path, startLine, lineCount, cancellationToken);
                    return Result(result, result.Source is null ? [] : [new AiSourceDto(result.Source.Path, result.Source.StartLine)]);
                }
                case "get_my_permissions":
                {
                    if (!OnlyProperties(arguments)) return Status("invalid_arguments");
                    var snapshot = await permissions.GetAsync(userId, cancellationToken);
                    return Result(new
                    {
                        status = "ok",
                        isAdministrator = snapshot.IsAdministrator,
                        permissions = snapshot.IsAdministrator ? IdentityPermissions.All : snapshot.Permissions,
                        note = "仅代表当前账号功能权限；不包含其他账号、SBU分配或页面状态。"
                    });
                }
                case "list_available_queries":
                {
                    if (!OnlyProperties(arguments, "query", "offset")) return Status("invalid_arguments");
                    var offset = 0;
                    if (arguments.TryGetProperty("offset", out _) &&
                        (!GetInt(arguments, "offset", out offset) || offset < 0)) return Status("invalid_arguments");
                    return LimitResult(await queries.ListAsync(GetText(arguments, "query", 200), offset, cancellationToken));
                }
                case "execute_query":
                {
                    if (!OnlyProperties(arguments, "id", "parameters", "filters")) return Status("invalid_arguments");
                    var id = GetText(arguments, "id", 200);
                    if (string.IsNullOrWhiteSpace(id) || !arguments.TryGetProperty("parameters", out var parameters) ||
                        parameters.ValueKind != JsonValueKind.Object)
                        return Status("invalid_arguments");
                    JsonElement? filters = null;
                    if (arguments.TryGetProperty("filters", out var filterValue))
                    {
                        if (filterValue.ValueKind != JsonValueKind.Object || !ValidFilters(filterValue))
                            return Status("invalid_arguments");
                        filters = filterValue;
                    }
                    return LimitResult(await queries.ExecuteAsync(id, parameters, filters, cancellationToken));
                }
                default:
                    return Status("unknown_tool");
            }
        }
        catch (JsonException)
        {
            return Status("invalid_arguments");
        }
        catch (ArgumentException)
        {
            return Status("invalid_arguments");
        }
    }

    private AssistantToolResult Result(object value, IReadOnlyList<AiSourceDto>? sources = null)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
        if (bytes.Length > limits.MaxToolResultBytes)
            return Status("too_large", "结果超过单次工具数据上限，未返回任何部分内容。请缩小查询范围；不能据此计算全局最大值或排名。");
        return new AssistantToolResult(System.Text.Encoding.UTF8.GetString(bytes), sources ?? []);
    }

    private AssistantToolResult LimitResult(AssistantToolResult result)
        => System.Text.Encoding.UTF8.GetByteCount(result.Content) > limits.MaxToolResultBytes
            ? Status("too_large", "结果超过单次工具数据上限，未返回任何部分内容。请缩小查询范围；不能据此计算全局最大值或排名。")
            : result;

    private static bool ValidFilters(JsonElement filters)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in filters.EnumerateObject())
        {
            if (seen.Count >= 20 || string.IsNullOrWhiteSpace(property.Name) || property.Name.Length > 100 ||
                property.Name.Any(char.IsControl) || !seen.Add(property.Name)) return false;
            if (property.Value.ValueKind != JsonValueKind.Object)
            {
                if (!ValidFilterScalar(property.Value)) return false;
                continue;
            }
            var operations = property.Value.EnumerateObject().ToArray();
            if (operations.Length != 1) return false;
            var operation = operations[0];
            if (operation.Name == "eq")
            {
                if (!ValidFilterScalar(operation.Value)) return false;
            }
            else if (operation.Name == "contains")
            {
                if (operation.Value.ValueKind != JsonValueKind.String ||
                    string.IsNullOrWhiteSpace(operation.Value.GetString()) || !ValidFilterScalar(operation.Value)) return false;
            }
            else return false;
        }
        return true;
    }

    private static bool ValidFilterScalar(JsonElement value)
        => value.ValueKind is JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False or JsonValueKind.Null ||
           value.ValueKind == JsonValueKind.String && value.GetString()!.Length <= 1000 &&
           !value.GetString()!.Any(char.IsControl);

    private static AssistantToolResult Status(string status, string? message = null)
        => new(JsonSerializer.Serialize(new { status, message }, JsonOptions), []);

    private static bool OnlyProperties(JsonElement arguments, params string[] names)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        return arguments.EnumerateObject().All(property => names.Contains(property.Name, StringComparer.Ordinal) && seen.Add(property.Name));
    }

    private static string? GetText(JsonElement arguments, string name, int maxLength)
    {
        if (!arguments.TryGetProperty(name, out var property)) return null;
        if (property.ValueKind != JsonValueKind.String) throw new ArgumentException("Invalid tool argument.");
        var text = property.GetString()!.Trim();
        if (text.Length > maxLength || text.Any(char.IsControl)) throw new ArgumentException("Invalid tool argument.");
        return text.Length == 0 ? null : text;
    }

    private static bool GetInt(JsonElement arguments, string name, out int value)
    {
        value = 0;
        return arguments.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out value);
    }
}
