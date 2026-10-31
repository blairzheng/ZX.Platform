using ZX.Platform.LowCode.Application.Metadata;

namespace ZX.Platform.LowCode.Tests;

/// <summary>
/// 假的"数据库现状"。测试里用 <see cref="Apply"/> 模拟 DDL 执行，
/// 这样"生成变更 → 执行 → 再发布"的完整回路可以在内存里跑通。
/// </summary>
internal sealed class InMemorySchemaInspector : ISchemaInspector
{
    private readonly Dictionary<string, HashSet<string>> _tables = new(StringComparer.OrdinalIgnoreCase);

    public int InspectCount { get; private set; }

    public Task<TableShape> InspectAsync(string tableName, CancellationToken cancellationToken)
    {
        InspectCount++;
        return Task.FromResult(_tables.TryGetValue(tableName, out var columns)
            ? new TableShape(true, [.. columns.Select(name => new TableColumn(name, string.Empty))])
            : TableShape.Missing);
    }

    /// <summary>模拟执行建表：表存在，且带上保留列。</summary>
    public void CreateTable(string tableName, params string[] columns)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ID", "CREATED_AT_UTC", "CREATED_BY_USER_ID",
            "UPDATED_AT_UTC", "UPDATED_BY_USER_ID", "IS_DELETED", "REVISION"
        };

        foreach (var column in columns) set.Add(column);
        _tables[tableName] = set;
    }

    /// <summary>模拟执行加列。</summary>
    public void AddColumn(string tableName, string column)
    {
        if (!_tables.TryGetValue(tableName, out var set))
            throw new InvalidOperationException($"表 {tableName} 还不存在，无法加列。");

        set.Add(column);
    }

    public void DropTable(string tableName) => _tables.Remove(tableName);
}
