namespace ZX.Platform.LowCode.Application.Metadata;

/// <summary>物理表里的一个列。</summary>
public sealed record TableColumn(string Name, string DataType);

/// <summary>物理表当前的样子。<c>Exists = false</c> 表示表还没建。</summary>
public sealed record TableShape(bool Exists, IReadOnlyList<TableColumn> Columns)
{
    public static TableShape Missing { get; } = new(false, []);

    /// <summary>测试用：只关心列名时不必写类型。</summary>
    public static TableShape Of(params string[] columns) =>
        new(true, [.. columns.Select(name => new TableColumn(name, string.Empty))]);

    public bool HasColumn(string column) =>
        Columns.Any(item => string.Equals(item.Name, column, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// 问数据库"这张表现在长什么样"。
///
/// 为什么不拿"上一次发布的定义"当基线：生产环境的 DDL 是导出给 DBA 执行的，
/// 什么时候执行、有没有顺手加过列、是不是只执行了一半，平台都不知道。
/// 一旦拿发布定义做 diff，DBA 提前把列加好了，平台仍会认为"缺这一列"，
/// 于是反复生成同一条 ALTER，这个实体就永远发布不出去。
///
/// 以库为准还顺带解决另一件事：加列成功后不需要"回写基线"，因为下一次 diff 的输入本身就变了。
/// </summary>
public interface ISchemaInspector
{
    Task<TableShape> InspectAsync(string tableName, CancellationToken cancellationToken);
}
