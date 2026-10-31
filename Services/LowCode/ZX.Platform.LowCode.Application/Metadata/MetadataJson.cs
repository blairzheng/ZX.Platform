using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using ZX.Platform.LowCode.Domain;

namespace ZX.Platform.LowCode.Application.Metadata;

/// <summary>
/// 元数据 JSON 的序列化规则。设计器、手写 JSON、运行时读的是同一份格式，
/// 所以序列化选项只能有一处定义。
/// </summary>
public static class MetadataJson
{
    public static JsonSerializerOptions Options { get; } = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            // 中文不转义成 \uXXXX，否则元数据在库里没法直接看
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        options.Converters.Add(new FieldTypeConverter());
        options.Converters.Add(new ScopeModeConverter());
        return options;
    }

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    public static T Deserialize<T>(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, Options)
                   ?? throw new JsonException("元数据 JSON 为空。");
        }
        catch (JsonException ex)
        {
            throw new LowCodeException(LowCodeErrorCodes.ValidationFailed, $"元数据 JSON 无法解析：{ex.Message}");
        }
    }

    private sealed class FieldTypeConverter : JsonConverter<FieldType>
    {
        public override FieldType Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        {
            var raw = reader.GetString();
            return FieldTypes.TryParse(raw, out var parsed)
                ? parsed
                : throw new JsonException($"未知字段类型：{raw}");
        }

        public override void Write(Utf8JsonWriter writer, FieldType value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.ToWireName());
    }

    private sealed class ScopeModeConverter : JsonConverter<ScopeMode>
    {
        public override ScopeMode Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        {
            var raw = reader.GetString();
            return ScopeModes.TryParse(raw, out var parsed)
                ? parsed
                : throw new JsonException($"未知数据范围模式：{raw}");
        }

        public override void Write(Utf8JsonWriter writer, ScopeMode value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.ToWireName());
    }
}
