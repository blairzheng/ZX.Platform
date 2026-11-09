using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Migrations;
using ZX.Platform.Identity.Infrastructure.Persistence;

namespace ZX.Platform.Identity.Api.Persistence;

/// <summary>默认离线生成统一迁移；显式指定 --development 时读取本机测试库配置。</summary>
public sealed class PlatformIdentityDesignTimeFactory : IDesignTimeDbContextFactory<PlatformIdentityDbContext>
{
    public PlatformIdentityDbContext CreateDbContext(string[] args)
    {
        if (args.Length == 0)
            return CreateContext(
                "User Id=SQL_GENERATION_ONLY;Password=unused;Data Source=127.0.0.1:1/SQL_GENERATION_ONLY;Connection Timeout=1",
                PlatformIdentityDbContext.DefaultSchema, development: false);

        if (args is not ["--development"])
            throw new InvalidOperationException("迁移参数仅支持 --development；生成迁移和发布 SQL 时不传此参数。");

        var configuration = new ConfigurationBuilder()
            .AddUserSecrets<PlatformIdentityDesignTimeFactory>(optional: false)
            .Build();
        return CreateDevelopmentContext(configuration);
    }

    public static PlatformIdentityDbContext CreateDevelopmentContext(IConfiguration configuration)
    {
        var connection = configuration.GetConnectionString("IdentityDatabase");
        if (string.IsNullOrWhiteSpace(connection))
            throw new InvalidOperationException("请先在 Identity.Api 的 User Secrets 中配置测试库 ConnectionStrings:IdentityDatabase。");

        var schema = configuration["IdentityDatabase:Schema"]?.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(schema))
            throw new InvalidOperationException("请先在 Identity.Api 的 User Secrets 中配置测试库 IdentityDatabase:Schema。");
        PlatformIdentityDbContext.ValidateSchema(schema);
        return CreateContext(connection, schema, development: true);
    }

    private static PlatformIdentityDbContext CreateContext(string connection, string schema, bool development)
    {
        var options = new DbContextOptionsBuilder<PlatformIdentityDbContext>();
        // 模型和 snapshot 始终使用统一源 schema，避免个人测试库配置污染迁移文件。
        PlatformIdentityDbContext.Configure(options, connection, PlatformIdentityDbContext.DefaultSchema);
        if (development)
        {
            // 保留 Configure 中的统一历史表映射，只替换开发库的目标 owner。
            options.UseOracle(connection, oracle =>
                oracle.MigrationsHistoryTable(PlatformIdentityDbContext.HistoryTable, schema));
            options.ReplaceService<IMigrationsSqlGenerator, DevelopmentOracleMigrationsSqlGenerator>();
        }
        return new PlatformIdentityDbContext(options.Options,
            new IdentityDatabaseSchema(PlatformIdentityDbContext.DefaultSchema));
    }
}
