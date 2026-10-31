using System.Data;
using Dapper;
using Microsoft.EntityFrameworkCore;
using ZX.Platform.LowCode.Application.Metadata;
using ZX.Platform.LowCode.Infrastructure.Persistence;

namespace ZX.Platform.LowCode.Infrastructure.Schema;

/// <summary>
/// 读 Oracle 数据字典，回答"这张表现在长什么样"。
///
/// 走 <c>USER_TAB_COLUMNS</c> / <c>ALL_TAB_COLUMNS</c> 而不是 <c>DBMS_METADATA</c>：
/// 前者只要有 SELECT 权限就能读，后者要额外的包权限，在只给业务账号
/// <c>CREATE TABLE</c> 的环境里根本调不通。
/// </summary>
public sealed class OracleSchemaInspector(LowCodeDbContext db, string? schema = null) : ISchemaInspector
{
    private const string UserSql = """
        SELECT COLUMN_NAME AS Name, DATA_TYPE AS DataType
        FROM USER_TAB_COLUMNS
        WHERE TABLE_NAME = :tableName
        ORDER BY COLUMN_ID
        """;

    private const string AllSql = """
        SELECT COLUMN_NAME AS Name, DATA_TYPE AS DataType
        FROM ALL_TAB_COLUMNS
        WHERE OWNER = :owner AND TABLE_NAME = :tableName
        ORDER BY COLUMN_ID
        """;

    public async Task<TableShape> InspectAsync(string tableName, CancellationToken cancellationToken)
    {
        // Oracle 的数据字典里标识符一律大写，带引号建的表也存大写。
        var table = tableName.ToUpperInvariant();

        var connection = db.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose) await connection.OpenAsync(cancellationToken);

        try
        {
            var command = schema is null
                ? new CommandDefinition(UserSql, new { tableName = table }, cancellationToken: cancellationToken)
                : new CommandDefinition(
                    AllSql,
                    new { owner = schema.ToUpperInvariant(), tableName = table },
                    cancellationToken: cancellationToken);

            var rows = (await connection.QueryAsync<ColumnRow>(command)).ToList();
            return rows.Count == 0
                ? TableShape.Missing
                : new TableShape(true, [.. rows.Select(row => new TableColumn(row.Name, row.DataType))]);
        }
        finally
        {
            // 连接是 EF 的，我们打开的就由我们关掉；本来就是开的（在事务里）就别动。
            if (shouldClose) await connection.CloseAsync();
        }
    }

    /// <summary>Dapper 的映射目标。用可变类而不是 record，避免依赖构造器绑定。</summary>
    private sealed class ColumnRow
    {
        public string Name { get; set; } = string.Empty;
        public string DataType { get; set; } = string.Empty;
    }
}
