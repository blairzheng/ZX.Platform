using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Oracle.ManagedDataAccess.Client;
using System.Net.Sockets;
using ZX.Platform.Identity.Api.Controllers;
using ZX.Platform.Identity.Api.Extensions;
using ZX.Platform.Identity.Infrastructure.ExternalData;

namespace ZX.Platform.Identity.AuthChecks;

internal static class ExternalOracleChecks
{
    public static async Task RunAsync()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ExternalOracle:Sources:MesE"] = "User Id=example;Password=unused;Data Source=unused",
            ["ExternalOracle:Sources:MesF"] = "User Id=example;Password=unused;Data Source=unused",
            ["ConnectionStrings:IdentityDatabase"] = "User Id=identity;Password=unused;Data Source=unused"
        }).Build();
        using var services = new ServiceCollection()
            .AddExternalOracleDataSources(configuration)
            .BuildServiceProvider();
        var factory = services.GetRequiredService<IExternalOracleConnectionFactory>();

        try
        {
            await factory.OpenAsync("IdentityDatabase", CancellationToken.None);
            throw new InvalidOperationException("External connections must not fall back to the Identity database.");
        }
        catch (InvalidOperationException exception) when (exception.Message.Contains("未配置外部 Oracle 数据源", StringComparison.Ordinal)) { }

        var invalid = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ExternalOracle:Sources:MesE"] = ""
        }).Build();
        try
        {
            new ServiceCollection().AddExternalOracleDataSources(invalid);
            throw new InvalidOperationException("Blank external connection strings must fail at startup.");
        }
        catch (InvalidOperationException exception) when (exception.Message.Contains("不能为空", StringComparison.Ordinal)) { }

        Console.WriteLine("External Oracle configuration checks passed; no database connection opened.");
    }

    public static async Task RunConnectivityAsync()
    {
        var configuration = new ConfigurationBuilder()
            .AddUserSecrets(typeof(AuthController).Assembly)
            .Build();
        var sources = configuration.GetSection("ExternalOracle:Sources")
            .Get<Dictionary<string, string>>() ?? new Dictionary<string, string>();
        if (sources.Count == 0)
            throw new InvalidOperationException("User Secrets 中没有配置 ExternalOracle:Sources。");

        var factory = new ExternalOracleConnectionFactory(sources);
        var failed = false;
        foreach (var alias in sources.Keys.Order(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await using var connection = await factory.OpenAsync(alias, timeout.Token);
                Console.WriteLine($"{alias}: connected ({connection.State}).");
            }
            catch (Exception exception)
            {
                failed = true;
                var reason = exception is OracleException oracle
                    ? $"ORA-{oracle.Number:D5}"
                    : exception.GetType().Name;
                var inner = exception.InnerException;
                var detail = inner switch
                {
                    OracleException nested => $", inner ORA-{nested.Number:D5}",
                    SocketException socket => $", socket {socket.SocketErrorCode}",
                    null => string.Empty,
                    _ => $", inner {inner.GetType().Name}"
                };
                Console.WriteLine($"{alias}: failed ({reason}{detail}).");
            }
        }

        if (failed)
            throw new InvalidOperationException("一个或多个外部 Oracle 数据源连接失败。");
    }
}
