using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ZX.Platform.LowCode.Infrastructure.Persistence;

/// <summary>
/// 设计时工厂，供 <c>dotnet ef</c> 使用。
/// 连接串走环境变量，不进仓库：优先低代码专用账号，其次复用 Identity 的连接。
/// </summary>
public sealed class LowCodeDbContextFactory : IDesignTimeDbContextFactory<LowCodeDbContext>
{
    public const string ConnectionVariable = "ConnectionStrings__LowCodeDatabase";
    public const string FallbackVariable = "ConnectionStrings__IdentityDatabase";
    public const string MigrationsTable = "LC_MIGRATIONS";

    public LowCodeDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionVariable)
                               ?? Environment.GetEnvironmentVariable(FallbackVariable);

        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException(
                $"请先设置环境变量 {ConnectionVariable}（或 {FallbackVariable}）再执行 ef 命令。");

        return new LowCodeDbContext(CreateOptions(connectionString));
    }

    public static DbContextOptions<LowCodeDbContext> CreateOptions(string connectionString) =>
        new DbContextOptionsBuilder<LowCodeDbContext>()
            .UseOracle(connectionString, oracle => oracle.MigrationsHistoryTable(MigrationsTable))
            .Options;
}
