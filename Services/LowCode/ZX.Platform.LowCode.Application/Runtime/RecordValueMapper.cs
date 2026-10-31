using System.Globalization;
using System.Text.Json;
using ZX.Platform.LowCode.Domain;

namespace ZX.Platform.LowCode.Application.Runtime;

/// <summary>
/// 数据库值 → 前端 JSON 值的转换。
///
/// 必须在服务端做：Oracle 把 boolean 存成 NUMBER(1)，把 date 存成 TIMESTAMP，
/// 直接吐给前端就是 0/1 和 ISO 长串，每个页面都得自己再转一遍，迟早转出不一致。
/// 这一层保证同一种字段类型在任何接口、任何页面上都只有一种表示。
/// </summary>
public static class RecordValueMapper
{
    /// <summary>把一行原始数据（列名为键）转成以字段 key 为键、已按类型规范化的字典。</summary>
    public static IReadOnlyDictionary<string, object?> ToWire(
        EntityDefinition entity,
        IReadOnlyDictionary<string, object?> row)
    {
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        foreach (var (column, value) in row)
        {
            var field = entity.FindFieldByColumn(column);

            if (field is null)
            {
                // 保留列（ID / REVISION / 审计字段）原样带出，键统一小写。
                result[column.ToLowerInvariant()] = value is DBNull ? null : value;
                continue;
            }

            result[field.Key] = Convert(field, value);
        }

        return result;
    }

    public static object? Convert(FieldDefinition field, object? value)
    {
        if (value is null or DBNull) return null;

        return field.Type switch
        {
            FieldType.Boolean => ToBoolean(value),
            FieldType.Number or FieldType.Amount or FieldType.Percent => ToDecimal(value),
            FieldType.Date => ToDate(value),
            FieldType.DateTime => ToDateTime(value),
            FieldType.MultiSelect => ToStringArray(value),
            _ => value.ToString()
        };
    }

    /// <summary>boolean 在库里是 NUMBER(1)，驱动可能给 decimal / short / int，逐个兜住。</summary>
    private static bool ToBoolean(object value) => value switch
    {
        bool flag => flag,
        decimal number => number != 0,
        double number => number != 0,
        byte number => number != 0,
        short number => number != 0,
        int number => number != 0,
        long number => number != 0,
        string text => text is "1" || bool.TryParse(text, out var parsed) && parsed,
        _ => throw Invalid(field: nameof(ToBoolean), value)
    };

    private static decimal ToDecimal(object value) => value switch
    {
        decimal number => number,
        double number => (decimal)number,
        float number => (decimal)number,
        int number => number,
        long number => number,
        short number => number,
        byte number => number,
        string text when decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) => parsed,
        _ => throw Invalid(nameof(ToDecimal), value)
    };

    /// <summary>date 一律 yyyy-MM-dd，不带时间也不带时区——盘点日期就该是一个日期。</summary>
    private static string ToDate(object value) => value switch
    {
        DateTime time => time.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        DateTimeOffset time => time.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        string text when DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            => parsed.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        _ => throw Invalid(nameof(ToDate), value)
    };

    /// <summary>
    /// datetime 按 UTC 输出，带 Z 后缀。库里存的是 TIMESTAMP（无时区），
    /// 写入时一律写 UTC，读出时补上 Z，否则前端会按本地时区再减一次。
    /// </summary>
    private static string ToDateTime(object value) => value switch
    {
        DateTime time => DateTime.SpecifyKind(time, DateTimeKind.Utc)
            .ToString("yyyy-MM-ddTHH:mm:ss'Z'", CultureInfo.InvariantCulture),
        DateTimeOffset time => time.ToUniversalTime()
            .ToString("yyyy-MM-ddTHH:mm:ss'Z'", CultureInfo.InvariantCulture),
        string text when DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var parsed)
            => DateTime.SpecifyKind(parsed, DateTimeKind.Utc)
                .ToString("yyyy-MM-ddTHH:mm:ss'Z'", CultureInfo.InvariantCulture),
        _ => throw Invalid(nameof(ToDateTime), value)
    };

    /// <summary>多选以 JSON 数组文本存 CLOB，读出来还原成数组，前端不用再 parse 一次。</summary>
    private static IReadOnlyList<string> ToStringArray(object value)
    {
        if (value is IEnumerable<string> items) return [.. items];

        var text = value.ToString();
        if (string.IsNullOrWhiteSpace(text)) return [];

        try
        {
            return JsonSerializer.Deserialize<string[]>(text) ?? [];
        }
        catch (JsonException)
        {
            // 脏数据不该让整页查询挂掉：返回空数组，原值在日志里由调用方排查。
            return [];
        }
    }

    private static LowCodeException Invalid(string field, object value) =>
        new(LowCodeErrorCodes.ValidationFailed,
            $"字段值无法转换（{field}，实际类型 {value.GetType().Name}），请检查历史数据。");
}
