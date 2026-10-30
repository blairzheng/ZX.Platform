using System.Data.Common;
using Oracle.ManagedDataAccess.Client;

namespace ZX.Platform.Identity.Infrastructure.ExternalData;

public sealed class ExternalOracleConnectionFactory : IExternalOracleConnectionFactory
{
    private readonly IReadOnlyDictionary<string, string> _connectionStrings;

    public ExternalOracleConnectionFactory(IReadOnlyDictionary<string, string> connectionStrings)
    {
        var sources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (alias, connectionString) in connectionStrings)
        {
            if (string.IsNullOrWhiteSpace(alias) || string.IsNullOrWhiteSpace(connectionString))
                throw new InvalidOperationException("外部 Oracle 数据源别名和连接字符串不能为空。");
            if (!sources.TryAdd(alias, connectionString))
                throw new InvalidOperationException($"外部 Oracle 数据源别名重复：{alias}。");
        }
        _connectionStrings = sources;
    }

    public async Task<DbConnection> OpenAsync(string alias, CancellationToken cancellationToken)
    {
        if (!_connectionStrings.TryGetValue(alias, out var connectionString))
            throw new InvalidOperationException($"未配置外部 Oracle 数据源：{alias}。");

        var connection = new OracleConnection(connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }
}
