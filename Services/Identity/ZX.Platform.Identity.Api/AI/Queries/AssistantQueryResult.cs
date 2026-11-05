using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using ZX.Platform.Identity.Application.AI;

namespace ZX.Platform.Identity.Api.AI.Queries;

internal static class AssistantQueryResult
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static AssistantToolResult Create(JsonElement data, JsonElement? filters, string id, bool requiresSbu,
        Guid? sbuId, DateTimeOffset retrievedAtUtc, int maxBytes, AiQueryResultShape resultShape)
    {
        if (!TryReadFilters(filters, out var conditions))
            return Status("invalid_arguments", "筛选只支持最多20个标量字段的eq或文本contains条件。");

        var payload = new Dictionary<string, object?>
        {
            ["status"] = "ok", ["queryId"] = id, ["retrievedAtUtc"] = retrievedAtUtc,
            ["requiresSbu"] = requiresSbu, ["sbuId"] = requiresSbu ? sbuId : null,
            ["filters"] = filters is { ValueKind: JsonValueKind.Object } ? filters.Value : null
        };
        var omitted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        JsonElement collection = default;
        long? sourceTotal = null;
        var metadata = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        if (data.ValueKind == JsonValueKind.Array)
        {
            collection = data;
        }
        else if (data.ValueKind == JsonValueKind.Object)
        {
            var properties = data.EnumerateObject().ToArray();
            if (properties.Select(property => property.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != properties.Length)
                return Status("unavailable", "查询接口返回重复字段，未采用含义不明确的数据。");
            var list = properties.FirstOrDefault(property => property.Name.Equals("list", StringComparison.OrdinalIgnoreCase));
            if (list.Value.ValueKind != JsonValueKind.Undefined)
            {
                if (list.Value.ValueKind != JsonValueKind.Array)
                    return Status("unavailable", "查询接口的list不是数组，未取得可处理的列表。");
                collection = list.Value;
                foreach (var property in properties)
                {
                    if (property.Name.Equals("list", StringComparison.OrdinalIgnoreCase)) continue;
                    if (property.Name.Equals("total", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!TryReadTotal(property.Value, out var total))
                            return Status("unavailable", "查询接口返回的total不是有效的非负整数，未判定列表完整性。");
                        sourceTotal = total;
                    }
                    else if (property.Name.Equals("rowKey", StringComparison.OrdinalIgnoreCase))
                        omitted.Add("rowKey");
                    else
                        metadata.Add(property.Name, property.Value);
                }
            }
        }

        if (collection.ValueKind != JsonValueKind.Array)
        {
            if (resultShape != AiQueryResultShape.Automatic)
                return Status("unavailable", "接口声明返回完整集合，但实际数据不是列表，未采用不一致的响应。");
            if (conditions.Count > 0)
                return Status("invalid_arguments", "当前接口返回详情或单值，不能应用列表字段筛选。");
            if (data.ValueKind == JsonValueKind.Undefined)
                return Status("unavailable", "查询接口没有返回可处理的数据。");
            payload["data"] = StripRowKey(data, omitted);
            payload["isComplete"] = false;
            payload["completeness"] = "not_applicable";
            payload["omittedFields"] = omitted.ToArray();
            payload["note"] = "这是接口返回的详情或单值，不是完整业务集合；不能据此计算全局最大值、总量或排名。仅移除omittedFields列出的字段，其余返回值保留。";
            return Encode(payload, maxBytes);
        }

        var originalRows = collection.EnumerateArray().ToArray();
        if (sourceTotal < originalRows.Length)
            return Status("unavailable", "查询接口的total小于实际返回行数，未采用不一致的数据。");
        var declaredComplete = resultShape is AiQueryResultShape.CompleteList or AiQueryResultShape.CompleteTree;
        if (declaredComplete && sourceTotal.HasValue && sourceTotal.Value != originalRows.Length)
            return Status("unavailable", "接口的完整集合声明与total不一致，未据此认定数据完整。");
        var isComplete = declaredComplete || sourceTotal.HasValue && sourceTotal.Value == originalRows.Length;
        var completeness = isComplete ? "complete" : sourceTotal.HasValue ? "partial" : "unknown";
        var completenessBasis = declaredComplete ? "endpoint_contract" : sourceTotal.HasValue ? "declared_total" : "not_declared";
        if (resultShape == AiQueryResultShape.CompleteTree)
        {
            if (conditions.Count > 0)
                return Status("invalid_arguments", "树形查询不能使用平面列表filters，否则会遗漏子节点。请读取完整children结构后分析。");
            if (!TryCountTreeNodes(collection, out var nodeCount))
                return Status("unavailable", "声明为树形的接口返回了不符合children数组结构的数据。");
            payload["collectionKind"] = "tree";
            payload["sourceTotal"] = sourceTotal;
            payload["sourceRowCount"] = originalRows.Length;
            payload["returnedRowCount"] = originalRows.Length;
            payload["rootCount"] = originalRows.Length;
            payload["nodeCount"] = nodeCount;
            payload["isComplete"] = true;
            payload["completeness"] = "complete";
            payload["completenessBasis"] = completenessBasis;
            payload["data"] = originalRows.Select(row => StripRowKey(row, omitted)).ToArray();
            payload["omittedFields"] = omitted.ToArray();
            if (metadata.Count > 0) payload["sourceMetadata"] = metadata;
            payload["note"] = "原接口声明返回完整可见树，data保留children层级。rootCount/sourceRowCount/returnedRowCount只计根节点，nodeCount递归计本次树中的节点，均不等于数据库实体总数。权限树可能含虚拟节点，统计菜单页面或权限时须按实际kind和permissionCode区分、去重。此完整性只针对原接口的可见视图，不扩展权限或数据范围。";
            return Encode(payload, maxBytes);
        }
        if (originalRows.Length == 0 && conditions.Count > 0)
            return Status("empty_result", "原接口本次返回的列表为空，没有可供筛选的记录，也无法从行数据校验筛选字段；不能把无记录等同于某项业务数值为0。");
        var objectRows = originalRows.All(row => row.ValueKind == JsonValueKind.Object);
        var columns = new List<string>();
        var knownColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in originalRows.Where(row => row.ValueKind == JsonValueKind.Object))
        {
            var rowColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in row.EnumerateObject())
            {
                if (!rowColumns.Add(property.Name))
                    return Status("unavailable", "查询结果的同一行包含重复字段，未采用含义不明确的数据。");
                if (knownColumns.Add(property.Name)) columns.Add(property.Name);
                if (knownColumns.Count > 128)
                    return Status("too_large", "查询结果超过128个字段，未返回部分字段或部分记录。请缩小查询范围。");
            }
        }
        if (conditions.Count > 0 && (!objectRows || conditions.Any(condition =>
                condition.Name.Equals("rowKey", StringComparison.OrdinalIgnoreCase) || !knownColumns.Contains(condition.Name))))
            return Status("invalid_arguments", "筛选字段不存在、已被移除，或列表行不是对象，未执行筛选。");

        foreach (var condition in conditions)
        {
            foreach (var row in originalRows)
            {
                if (!TryGetProperty(row, condition.Name, out var value)) continue;
                if (!IsScalar(value) || condition.Operation == "contains" && value.ValueKind is not (JsonValueKind.String or JsonValueKind.Null) ||
                    value.ValueKind == JsonValueKind.Number && !value.TryGetDecimal(out _))
                    return Status("invalid_arguments", "筛选只能应用于标量字段；contains只用于文本，数值须可按decimal精确比较。");
            }
        }

        var matchedRows = originalRows.Where(row => conditions.All(condition =>
            TryGetProperty(row, condition.Name, out var value) && Matches(value, condition))).ToArray();
        if (knownColumns.Contains("rowKey")) omitted.Add("rowKey");
        columns.RemoveAll(column => column.Equals("rowKey", StringComparison.OrdinalIgnoreCase));
        payload["sourceTotal"] = sourceTotal;
        payload["collectionKind"] = "list";
        payload["sourceRowCount"] = originalRows.Length;
        payload["returnedRowCount"] = matchedRows.Length;
        payload["matchedRowCount"] = matchedRows.Length;
        payload["isComplete"] = isComplete;
        payload["completeness"] = completeness;
        payload["completenessBasis"] = completenessBasis;
        payload["omittedFields"] = omitted.ToArray();
        if (metadata.Count > 0) payload["sourceMetadata"] = metadata;
        if (objectRows)
        {
            payload["columns"] = columns;
            payload["rows"] = matchedRows.Select(row => columns.Select(column =>
                TryGetProperty(row, column, out var value) ? (object?)value : null).ToArray()).ToArray();
        }
        else
        {
            // Scalar/array rows retain their structure. Any object row in a mixed collection loses only rowKey.
            payload["data"] = matchedRows.Select(row => StripRowKey(row, omitted)).ToArray();
        }
        payload["note"] = (isComplete
            ? (declaredComplete ? "原接口已声明返回完整可见列表；" : "原接口total与本次原始返回行数一致；") +
                "已对这份完整集合应用filters，可基于完整匹配记录计算，范围限于原接口可见集合。"
            : sourceTotal.HasValue
                ? "原接口只返回部分记录；filters只作用于本次返回的数据，matchedRowCount只是当前部分的匹配数，不支持据此计算全局最大值、总量或排名。"
                : "原接口未提供total，无法确认是否完整；filters只作用于本次返回的数据，不支持据此声称全局最大值、总量或排名。") +
            "sourceRowCount是原始返回行数，returnedRowCount和matchedRowCount是本次筛选后返回的行数。" +
            "未截断记录；仅移除omittedFields列出的字段。空值不是0，列缺失以null占位但不推断其实际值；嵌套值保持原结构。";
        return Encode(payload, maxBytes);
    }

    private static bool TryCountTreeNodes(JsonElement nodes, out long count)
    {
        count = 0;
        foreach (var node in nodes.EnumerateArray())
        {
            if (node.ValueKind != JsonValueKind.Object) return false;
            count++;
            if (!TryGetProperty(node, "children", out var children) || children.ValueKind == JsonValueKind.Null) continue;
            if (children.ValueKind != JsonValueKind.Array || !TryCountTreeNodes(children, out var descendants)) return false;
            count += descendants;
        }
        return true;
    }

    private static bool TryReadFilters(JsonElement? filters, out List<Condition> conditions)
    {
        conditions = [];
        if (!filters.HasValue || filters.Value.ValueKind == JsonValueKind.Null) return true;
        if (filters.Value.ValueKind != JsonValueKind.Object) return false;
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in filters.Value.EnumerateObject())
        {
            if (conditions.Count >= 20 || string.IsNullOrWhiteSpace(property.Name) || property.Name.Length > 100 ||
                property.Name.Any(char.IsControl) || !names.Add(property.Name)) return false;
            var value = property.Value;
            var operation = "eq";
            if (value.ValueKind == JsonValueKind.Object)
            {
                var operations = value.EnumerateObject().ToArray();
                if (operations.Length != 1 || operations[0].Name is not ("eq" or "contains")) return false;
                operation = operations[0].Name;
                value = operations[0].Value;
            }
            if (!IsScalar(value) || value.ValueKind == JsonValueKind.String &&
                (value.GetString()!.Length > 1000 || value.GetString()!.Any(char.IsControl)) ||
                value.ValueKind == JsonValueKind.Number && !value.TryGetDecimal(out _) ||
                operation == "contains" && (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString())))
                return false;
            conditions.Add(new Condition(property.Name, operation, value));
        }
        return true;
    }

    private static bool Matches(JsonElement actual, Condition condition)
    {
        var expected = condition.Value;
        if (condition.Operation == "contains")
            return actual.ValueKind == JsonValueKind.String &&
                actual.GetString()!.Contains(expected.GetString()!, StringComparison.OrdinalIgnoreCase);
        if (actual.ValueKind != expected.ValueKind) return false;
        return actual.ValueKind switch
        {
            JsonValueKind.String => string.Equals(actual.GetString(), expected.GetString(), StringComparison.OrdinalIgnoreCase),
            JsonValueKind.Number => actual.TryGetDecimal(out var left) && expected.TryGetDecimal(out var right) && left == right,
            JsonValueKind.True or JsonValueKind.False or JsonValueKind.Null => true,
            _ => false
        };
    }

    private static bool IsScalar(JsonElement value) => value.ValueKind is JsonValueKind.String or JsonValueKind.Number or
        JsonValueKind.True or JsonValueKind.False or JsonValueKind.Null;

    private static bool TryGetProperty(JsonElement value, string name, out JsonElement result)
    {
        if (value.ValueKind == JsonValueKind.Object)
            foreach (var property in value.EnumerateObject())
                if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    result = property.Value;
                    return true;
                }
        result = default;
        return false;
    }

    private static bool TryReadTotal(JsonElement value, out long total)
    {
        total = 0;
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDecimal(out var number) ||
            number < 0 || number > long.MaxValue || number != decimal.Truncate(number)) return false;
        total = (long)number;
        return true;
    }

    private static object StripRowKey(JsonElement value, HashSet<string> omitted)
    {
        if (value.ValueKind != JsonValueKind.Object) return value;
        var properties = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in value.EnumerateObject())
        {
            if (property.Name.Equals("rowKey", StringComparison.OrdinalIgnoreCase)) omitted.Add("rowKey");
            else properties.Add(property.Name, property.Value);
        }
        return properties;
    }

    private static AssistantToolResult Encode(object payload, int maxBytes)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
        return bytes.Length > maxBytes
            ? Status("too_large", "查询结果超过单次工具数据上限，未返回部分字段或部分记录。请缩小接口查询范围或filters；不能据此计算全局最大值或排名。")
            : new AssistantToolResult(Encoding.UTF8.GetString(bytes), []);
    }

    private static AssistantToolResult Status(string status, string message)
        => new(JsonSerializer.Serialize(new { status, message }, JsonOptions), []);

    private sealed record Condition(string Name, string Operation, JsonElement Value);
}
