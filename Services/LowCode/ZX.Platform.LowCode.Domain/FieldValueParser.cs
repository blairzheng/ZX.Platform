using System.Globalization;
using System.Text.Json;

namespace ZX.Platform.LowCode.Domain;

/// <summary>默认值解析。字符串是 JSON 里的唯一形态，按类型解析成具体值。</summary>
public static class FieldValueParser
{
    public const string NowToken = "$now";
    public const string CurrentUserIdToken = "$currentUserId";

    public static bool IsSpecial(string raw) => raw.StartsWith('$');

    /// <summary>特殊值只允许两个，且只在指定类型上成立——不做表达式。</summary>
    public static bool IsAllowedSpecial(FieldType type, string raw) => raw switch
    {
        NowToken => type is FieldType.Date or FieldType.DateTime,
        CurrentUserIdToken => type is FieldType.Relation,
        _ => false
    };

    /// <summary>按类型解析字面量。失败时返回 false，不抛异常。</summary>
    public static bool TryParseValue(FieldType type, string raw, IReadOnlyList<FieldOption> options, out object? value)
    {
        value = null;
        switch (type)
        {
            case FieldType.Text:
            case FieldType.LongText:
            case FieldType.AutoNumber:
                value = raw;
                return true;

            case FieldType.Select:
                if (options.All(option => !string.Equals(option.Value, raw, StringComparison.Ordinal))) return false;
                value = raw;
                return true;

            case FieldType.Number:
            case FieldType.Amount:
                if (!decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var number))
                    return false;
                value = number;
                return true;

            case FieldType.Percent:
                if (!decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var percent))
                    return false;
                if (percent is < 0 or > 100) return false;
                value = percent;
                return true;

            case FieldType.Boolean:
                if (!bool.TryParse(raw, out var flag)) return false;
                value = flag;
                return true;

            case FieldType.Date:
                if (!DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal, out var date))
                    return false;
                value = date;
                return true;

            case FieldType.DateTime:
                if (!DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal, out var timestamp))
                    return false;
                value = timestamp;
                return true;

            case FieldType.MultiSelect:
            case FieldType.Attachment:
                try
                {
                    using var document = JsonDocument.Parse(raw);
                    if (document.RootElement.ValueKind != JsonValueKind.Array) return false;
                }
                catch (JsonException)
                {
                    return false;
                }

                value = raw;
                return true;

            default:
                return false;
        }
    }
}
