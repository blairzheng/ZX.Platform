using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Oracle.ManagedDataAccess.Client;
using Oracle.ManagedDataAccess.Types;
using ZX.Platform.LowCode.Application.Runtime;
using ZX.Platform.LowCode.Domain;
using ZX.Platform.LowCode.Infrastructure.Oracle;

namespace ZX.Platform.LowCode.Infrastructure.Persistence;

/// <summary>
/// 业务表的读写实现：Dapper 取行 + 手写 SQL 生成。
///
/// 用 Dapper 而不是 EF 的原因只有一个：业务表在编译期不存在，没有实体类可以映射。
///
/// 三个 Oracle 专有细节在这里收口：
/// 1. 必须 BindByName = true，否则 Oracle 按位置绑定，:p0/:p1 全错位；
/// 2. CLOB / NUMBER 通过 GetValue 拿到的是 Oracle 的包装类型，不是 string / decimal，
///    在 DB 边界就换成普通 CLR 类型，上层不用认识 Oracle；
/// 3. ORA-00001 统一翻译成 600003。唯一性预检与写入之间必然有竞态，索引才是最终裁判。
/// </summary>
public sealed class DapperRecordGateway(LowCodeDbContext db) : IRecordGateway
{
    /// <summary>VARCHAR2 的绑定上限。超过它的字符串按 CLOB 绑，否则驱动直接拒绝。</summary>
    private const int VarcharLimit = 4000;

    public async Task<RecordPage> QueryAsync(QuerySpec spec, CancellationToken cancellationToken)
    {
        var select = SelectSqlBuilder.BuildSelect(spec);
        var count = SelectSqlBuilder.BuildCount(spec);
        var take = SelectSqlBuilder.ResolveTake(spec.Take);

        return await WithConnectionAsync(async connection =>
        {
            await using var countCommand = CreateCommand(connection, count);
            var total = Convert.ToInt32(await countCommand.ExecuteScalarAsync(cancellationToken) ?? 0);

            // 总数已知，后面这一页必然是空的就别再查一次。
            if (total == 0 || spec.Skip >= total) return RecordPage.Empty(spec.Skip, take);

            await using var selectCommand = CreateCommand(connection, select);
            await using var reader = await selectCommand.ExecuteReaderAsync(cancellationToken);

            var fieldCount = reader.FieldCount;
            var names = new string[fieldCount];
            for (var index = 0; index < fieldCount; index++) names[index] = reader.GetName(index);

            var rows = new List<IReadOnlyDictionary<string, object?>>();
            while (await reader.ReadAsync(cancellationToken))
            {
                var row = new Dictionary<string, object?>(fieldCount, StringComparer.OrdinalIgnoreCase);
                for (var index = 0; index < fieldCount; index++)
                    row[names[index]] = Normalize(reader.GetValue(index));

                rows.Add(row);
            }

            return new RecordPage(rows, total, spec.Skip, take);
        }, cancellationToken);
    }

    public Task CreateAsync(
        EntityDefinition entity,
        Guid id,
        IReadOnlyDictionary<string, object?> values,
        RecordScope scope,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var query = WriteSqlBuilder.BuildInsert(new InsertRequest
        {
            Entity = entity,
            Id = id,
            Context = new WriteContext(scope.UserId, scope.SbuId, now),
            Values = values
        });

        return ExecuteAsync(query, cancellationToken);
    }

    public async Task<bool> UpdateAsync(
        EntityDefinition entity,
        Guid id,
        int revision,
        IReadOnlyDictionary<string, object?> values,
        RecordScope scope,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var query = WriteSqlBuilder.BuildUpdate(new UpdateRequest
        {
            Entity = entity,
            Id = id,
            Revision = revision,
            Context = new WriteContext(scope.UserId, scope.SbuId, now),
            Values = values
        });

        return await ExecuteAsync(query, cancellationToken) > 0;
    }

    public async Task<bool> DeleteAsync(
        EntityDefinition entity,
        Guid id,
        int revision,
        RecordScope scope,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var query = WriteSqlBuilder.BuildSoftDelete(
            entity, id, revision, new WriteContext(scope.UserId, scope.SbuId, now));

        return await ExecuteAsync(query, cancellationToken) > 0;
    }

    private Task<int> ExecuteAsync(SqlQuery query, CancellationToken cancellationToken) =>
        WithConnectionAsync(async connection =>
        {
            try
            {
                await using var command = CreateCommand(connection, query);
                return await command.ExecuteNonQueryAsync(cancellationToken);
            }
            catch (OracleException ex) when (ex.Number == 1)
            {
                // ORA-00001：并发下唯一性预检放行了，索引拦住。对调用方就是 600003。
                throw new LowCodeException(
                    LowCodeErrorCodes.Conflict, "唯一约束冲突：同一编码的记录已存在。");
            }
        }, cancellationToken);

    /// <summary>
    /// 手写命令而不用 Dapper 的 CommandDefinition：Dapper 不会替我们打开 BindByName，
    /// 而按位置绑定会让所有命名参数静默错位——这种 bug 在生产上极难发现。
    /// </summary>
    private static OracleCommand CreateCommand(DbConnection connection, SqlQuery query)
    {
        var command = (OracleCommand)connection.CreateCommand();
        command.CommandText = query.Sql;
        command.BindByName = true;

        foreach (var parameter in query.Parameters)
            command.Parameters.Add(CreateParameter(parameter));

        return command;
    }

    /// <summary>
    /// CLR 值 → Oracle 参数。**必须显式指定 OracleDbType**：
    /// <c>new OracleParameter(name, value)</c> 只认它内部那张映射表，
    /// <see cref="Guid"/> 不在表里，会抛 <c>ArgumentException: Value does not fall within the expected range</c>。
    /// 而 ID / SBU_ID / 关联字段全是 RAW(16)，等于所有写操作都过不去。
    ///
    /// 字符串按长度分流：超过 4000 绑 CLOB。VARCHAR2 → CLOB 在 INSERT/UPDATE 里
    /// Oracle 允许隐式转换，反过来 CLOB → VARCHAR2 则要求长度放得下，所以只单向分流。
    /// </summary>
    private static OracleParameter CreateParameter(SqlParameter parameter)
    {
        var name = parameter.Name;

        return parameter.Value switch
        {
            null or DBNull => Input(name, OracleDbType.Varchar2, DBNull.Value),
            Guid guid => Input(name, OracleDbType.Raw, guid.ToByteArray()),
            byte[] bytes => Input(name, OracleDbType.Raw, bytes),
            bool flag => Input(name, OracleDbType.Int32, flag ? 1 : 0),
            string text when text.Length > VarcharLimit => Input(name, OracleDbType.Clob, text),
            string text => Input(name, OracleDbType.Varchar2, text),
            DateTimeOffset offset => Input(name, OracleDbType.TimeStampTZ, offset),
            DateTime time => Input(name, OracleDbType.TimeStamp, time),
            int number => Input(name, OracleDbType.Int32, number),
            long number => Input(name, OracleDbType.Int64, number),
            short number => Input(name, OracleDbType.Int16, number),
            decimal number => Input(name, OracleDbType.Decimal, number),
            double number => Input(name, OracleDbType.Double, number),
            float number => Input(name, OracleDbType.BinaryFloat, number),

            // 兜底：真出了没覆盖到的类型，宁可让驱动自己报错，也不要静默写成错值。
            _ => Input(name, OracleDbType.Varchar2, parameter.Value)
        };

        static OracleParameter Input(string name, OracleDbType type, object value) =>
            new(name, type, value, ParameterDirection.Input);
    }

    /// <summary>共用 EF 的连接；本来关着就由我们打开并关回去，在事务里开着就别动。</summary>
    private async Task<T> WithConnectionAsync<T>(
        Func<DbConnection, Task<T>> action, CancellationToken cancellationToken)
    {
        var connection = db.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose) await connection.OpenAsync(cancellationToken);

        try
        {
            return await action(connection);
        }
        finally
        {
            if (shouldClose) await connection.CloseAsync();
        }
    }

    /// <summary>
    /// Oracle 包装类型 → 普通 CLR 类型。上层只认 string / decimal / DateTime / Guid。
    /// RAW(16) 读出来是 byte[]，不换掉的话序列化成 JSON 就是一段 base64，
    /// 前端拿 ID 去查详情会 404。
    /// </summary>
    private static object? Normalize(object? value) => value switch
    {
        null or DBNull => null,
        OracleClob clob => clob.IsNull ? null : clob.Value,
        OracleDecimal number => number.IsNull ? (object?)null : number.Value,
        OracleDate date => date.IsNull ? (object?)null : date.Value,
        OracleString text => text.IsNull ? null : text.Value,
        byte[] { Length: 16 } raw => new Guid(raw),
        _ => value
    };
}
