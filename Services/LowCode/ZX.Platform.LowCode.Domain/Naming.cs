using System.Text.RegularExpressions;

namespace ZX.Platform.LowCode.Domain;

/// <summary>命名规则：实体、应用、字段的统一约束。</summary>
public static class Naming
{
    /// <summary>Oracle 旧版标识符上限。取 30 是为了兼容现有工具链与客户端。</summary>
    public const int MaxIdentifierLength = 30;

    private static readonly Regex CodePattern = new("^[a-z][a-z0-9_]{0,29}$", RegexOptions.Compiled);

    public const string TablePrefix = "LC_";

    public static bool IsValidCode(string? code) => !string.IsNullOrEmpty(code) && CodePattern.IsMatch(code);

    /// <summary>物理表名：<c>LC_{APP}_{ENTITY}</c>，全大写。</summary>
    public static string TableName(string appCode, string entityCode) =>
        $"{TablePrefix}{appCode}_{entityCode}".ToUpperInvariant();

    public static bool IsValidTableName(string tableName) =>
        !string.IsNullOrEmpty(tableName) && tableName.Length <= MaxIdentifierLength;

    /// <summary>权限码：<c>{app}.{entity}.{action}</c>。</summary>
    public static string PermissionCode(string appCode, string entityCode, string action) =>
        $"{appCode}.{entityCode}.{action}";

    /// <summary>索引名：<c>IX_{TABLE}_{COLUMN}</c>，超长时由调用方截断。</summary>
    public static string IndexName(string tableName, string column) =>
        $"IX_{tableName}_{column}".ToUpperInvariant();

    public static string UniqueIndexName(string tableName, string column) =>
        $"UX_{tableName}_ACTIVE_{column}".ToUpperInvariant();

    public static string PrimaryKeyName(string tableName) => $"PK_{tableName}".ToUpperInvariant();
}

/// <summary>每张业务表固定包含的列。字段 key 不得与它们冲突（不区分大小写）。</summary>
public static class ReservedColumns
{
    public const string Id = "ID";
    public const string SbuId = "SBU_ID";
    public const string CreatedAtUtc = "CREATED_AT_UTC";
    public const string CreatedByUserId = "CREATED_BY_USER_ID";
    public const string UpdatedAtUtc = "UPDATED_AT_UTC";
    public const string UpdatedByUserId = "UPDATED_BY_USER_ID";
    public const string IsDeleted = "IS_DELETED";
    public const string Revision = "REVISION";

    public static IReadOnlySet<string> All { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        Id,
        SbuId,
        CreatedAtUtc,
        CreatedByUserId,
        UpdatedAtUtc,
        UpdatedByUserId,
        IsDeleted,
        Revision
    };

    public static bool IsReserved(string key) => All.Contains(key);
}

/// <summary>实体的数据范围模式。relation 模式明确不做——推导归属在 MES 里必然出错。</summary>
public enum ScopeMode
{
    None,
    Column
}

public static class ScopeModes
{
    public const string DefaultScopeField = ReservedColumns.SbuId;

    public static bool TryParse(string? value, out ScopeMode mode)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case null or "":
            case "none":
                mode = ScopeMode.None;
                return true;
            case "column":
                mode = ScopeMode.Column;
                return true;
            default:
                mode = default;
                return false;
        }
    }

    public static string ToWireName(this ScopeMode mode) => mode switch
    {
        ScopeMode.None => "none",
        ScopeMode.Column => "column",
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null)
    };
}
