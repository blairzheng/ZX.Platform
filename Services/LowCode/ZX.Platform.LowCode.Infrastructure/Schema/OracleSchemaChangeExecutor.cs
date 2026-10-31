using System.Data;
using Microsoft.EntityFrameworkCore;
using Oracle.ManagedDataAccess.Client;
using ZX.Platform.LowCode.Application.Schema;
using ZX.Platform.LowCode.Domain;
using ZX.Platform.LowCode.Infrastructure.Persistence;

namespace ZX.Platform.LowCode.Infrastructure.Schema;

/// <summary>
/// 在测试环境直接执行 DDL。
///
/// 逐条执行并记录第几条失败：Oracle 的 DDL 每条隐式提交，失败时前面的语句已经生效，
/// 不能伪装成"什么都没发生"。调用方拿到的错误信息必须包含足够线索，
/// 否则重试时会撞上"对象已存在"，人就开始手工乱改库了。
/// </summary>
public sealed class OracleSchemaChangeExecutor(LowCodeDbContext db) : ISchemaChangeExecutor
{
    public async Task ExecuteAsync(IReadOnlyList<string> statements, CancellationToken cancellationToken)
    {
        if (statements.Count == 0) return;

        var connection = db.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose) await connection.OpenAsync(cancellationToken);

        try
        {
            for (var index = 0; index < statements.Count; index++)
            {
                try
                {
                    await using var command = connection.CreateCommand();
                    command.CommandText = statements[index];
                    await command.ExecuteNonQueryAsync(cancellationToken);
                }
                catch (OracleException ex)
                {
                    throw new LowCodeException(LowCodeErrorCodes.StateInvalid,
                        $"第 {index + 1}/{statements.Count} 条 DDL 执行失败：{ex.Message}"
                        + "（前面的语句已生效，Oracle 的 DDL 不能回滚，请先核对库结构再重试）");
                }
            }
        }
        finally
        {
            if (shouldClose) await connection.CloseAsync();
        }
    }
}
