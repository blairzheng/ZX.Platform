using System.Security.Cryptography;
using System.Text;

namespace ZX.Platform.LowCode.Infrastructure.Oracle;

/// <summary>
/// Oracle 标识符处理。全部大写 + 双引号，避免 Oracle 的大小写折叠把列名变成别的意思。
/// </summary>
public static class OracleIdentifier
{
    /// <summary>Oracle 标识符上限。取 30 是为了兼容旧工具链。</summary>
    public const int MaxLength = 30;

    /// <summary>给标识符加双引号并转大写。含双引号或空白直接拒绝——这是注入的唯一入口。</summary>
    public static string Quote(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
            throw new ArgumentException("标识符不能为空。", nameof(identifier));

        if (identifier.Contains('"') || identifier.Any(char.IsWhiteSpace))
            throw new ArgumentException($"非法标识符：{identifier}", nameof(identifier));

        return $"\"{identifier.ToUpperInvariant()}\"";
    }

    /// <summary>schema.table 形式。schema 为空时只输出表名，由连接账号决定落在哪个 schema。</summary>
    public static string QuoteQualified(string? schema, string table) =>
        string.IsNullOrWhiteSpace(schema) ? Quote(table) : $"{Quote(schema)}.{Quote(table)}";

    /// <summary>
    /// 超长时截断并追加确定性哈希后缀。同一输入永远得到同一结果，
    /// 否则重复生成 DDL 会得到不同的约束名。
    /// </summary>
    public static string Shorten(string name, int maxLength = MaxLength)
    {
        if (name.Length <= maxLength) return name;

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(name)))[..6];
        var keep = maxLength - hash.Length - 1;
        return $"{name[..keep]}_{hash}";
    }

    public static string QuoteShortened(string name, int maxLength = MaxLength) => Quote(Shorten(name, maxLength));
}
