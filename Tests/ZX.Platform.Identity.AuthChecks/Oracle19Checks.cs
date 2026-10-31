using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ZX.Platform.Identity.Api.Extensions;
using ZX.Platform.Identity.Infrastructure.Persistence;
using ZX.Platform.Identity.Infrastructure.Persistence.Interceptors;

namespace ZX.Platform.Identity.AuthChecks;

internal static class Oracle19Checks
{
    private const string GenerationConnection = "User Id=SQL_GENERATION_ONLY;Password=unused;Data Source=127.0.0.1:1/SQL_GENERATION_ONLY";

    public static PlatformIdentityDbContext CreateContext(string schema = PlatformIdentityDbContext.DefaultSchema)
    {
        var options = new DbContextOptionsBuilder<PlatformIdentityDbContext>();
        PlatformIdentityDbContext.Configure(options, GenerationConnection, schema);
        return new PlatformIdentityDbContext(options.Options, new IdentityDatabaseSchema(schema));
    }

    public static void Run(string? sqlOutputPath = null)
    {
        // 模型、迁移和 SQL 检查均离线执行，不打开数据库连接。
        using var db = CreateContext();
        Check.That(!db.Database.HasPendingModelChanges(), "The unified Oracle 19c mappings must match the existing snapshot.");
        Check.That(db.Model.GetEntityTypes().All(entity => entity.GetSchema() == "ZX_IDENTITY"),
            "Oracle 19c business tables must resolve explicitly to ZX_IDENTITY even when another account logs in.");
        CheckNumericBooleans(db);
        var migrations = db.Database.GetMigrations().ToArray();

        // 只校验"基线是第一条、之后是一条线性链"，不写死总数：
        // 每加一个迁移都要改一次这里，除了制造冲突没有别的作用。
        Check.That(migrations.Length >= 2 && migrations[0] == "20261027000000_InitialIdentitySchema" &&
            migrations[1] == "20261028084818_ReplaceFactoriesWithSbus" &&
            migrations.SequenceEqual(migrations.Order(StringComparer.Ordinal)),
            "The project must have one Oracle 19c migration chain starting at its new baseline.");

        var migrator = db.GetService<IMigrator>();
        var baselineSql = migrator.GenerateScript(toMigration: migrations[0]);
        var upgradeSql = migrator.GenerateScript(fromMigration: migrations[0], toMigration: migrations[1]);
        var sql = migrator.GenerateScript();
        Check.That(!sql.Contains("BOOLEAN", StringComparison.OrdinalIgnoreCase) &&
            !sql.Contains("IS FALSE", StringComparison.OrdinalIgnoreCase) &&
            !sql.Contains("IS TRUE", StringComparison.OrdinalIgnoreCase),
            "Oracle 19c migrations must not contain SQL Boolean types or predicates.");
        Check.That(baselineSql.Contains("\"ZX_IDENTITY\".\"ZX_IDENTITY_USER\"") &&
            baselineSql.Contains("\"ZX_IDENTITY\".\"ZX_IDENTITY_MIGRATIONS\"") &&
            baselineSql.Contains("\"IS_DELETED\" = 0") &&
            !baselineSql.Contains("DROP TABLE", StringComparison.OrdinalIgnoreCase),
            "Oracle 19c baseline must create scoped MOM tables and conditional uniqueness without deleting tables.");
        Check.That(upgradeSql.Contains("DROP TABLE \"ZX_IDENTITY\".\"ZX_IDENTITY_FACTORY\"") &&
            upgradeSql.Contains("DROP TABLE \"ZX_IDENTITY\".\"ZX_IDENTITY_USER_FACTORY\"") &&
            upgradeSql.Contains("\"ZX_IDENTITY\".\"ZX_IDENTITY_SBU\"") &&
            upgradeSql.Contains("\"ZX_IDENTITY\".\"ZX_IDENTITY_USER_SBU\"") &&
            upgradeSql.Contains("20261028084818_ReplaceFactoriesWithSbus"),
            "The SBU upgrade must replace only the former factory tables and record its migration.");

        var query = db.Users.Where(user => user.IsEnabled).OrderBy(user => user.Id).Take(10)
            .Select(user => new { user.Id, HasSbu = db.UserSbus.Any(link => link.UserId == user.Id) })
            .ToQueryString();
        Check.That(query.Contains("\"ZX_IDENTITY\".\"ZX_IDENTITY_USER\"") &&
            query.Contains("\"ZX_IDENTITY\".\"ZX_IDENTITY_USER_SBU\"") &&
            !query.Contains("TRUE", StringComparison.OrdinalIgnoreCase) &&
            !query.Contains("FALSE", StringComparison.OrdinalIgnoreCase),
            "Oracle 19c queries must qualify cross-schema tables and translate Boolean filters and projections to numeric SQL.");

        var originalSchemaEnvironment = Environment.GetEnvironmentVariable("IdentityDatabase__Schema");
        foreach (var schema in new string?[] { null, "SAJET", "MOM_PROD" })
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:IdentityDatabase"] = GenerationConnection,
                ["IdentityDatabase:Schema"] = schema
            }).Build();
            var services = new ServiceCollection();
            services.AddSingleton(TimeProvider.System);
            services.AddScoped<AuditableEntitySaveChangesInterceptor>();
            services.AddIdentityDatabase(configuration);
            using var provider = services.BuildServiceProvider();
            using var scope = provider.CreateScope();
            var local = scope.ServiceProvider.GetRequiredService<PlatformIdentityDbContext>();
            Check.That(ReferenceEquals(local, scope.ServiceProvider.GetRequiredService<PlatformIdentityDbContext>()),
                "Repositories must share the same scoped DbContext.");
            CheckNumericBooleans(local);
            Check.That(local.Model.GetEntityTypes().All(entity => entity.GetSchema() == (schema ?? "ZX_IDENTITY")),
                "Each deployment must use the configured table owner; local development defaults to ZX_IDENTITY.");
        }

        // 同进程按 schema 分别缓存模型，不能串用首次创建的模型。
        foreach (var schema in new[] { "MOM_PROD", "ZX_IDENTITY", "SAJET" })
        {
            using var custom = CreateContext(schema);
            var customQuery = custom.Users.Where(user => user.IsEnabled).ToQueryString();
            Check.That(custom.Model.GetEntityTypes().All(entity => entity.GetSchema() == schema) &&
                customQuery.Contains($"\"{schema}\".\"ZX_IDENTITY_USER\"") &&
                !customQuery.Contains("TRUE", StringComparison.OrdinalIgnoreCase),
                "Custom schemas must use their own qualified Oracle 19c model and query SQL.");
        }
        Check.That(Environment.GetEnvironmentVariable("IdentityDatabase__Schema") == originalSchemaEnvironment,
            "Runtime database registration must not mutate the process-wide migration schema.");
        Check.That(typeof(PlatformIdentityDbContext).Assembly.GetTypes()
            .Count(type => !type.IsAbstract && typeof(DbContext).IsAssignableFrom(type)) == 1,
            "The infrastructure must contain exactly one concrete DbContext.");
        if (sqlOutputPath is not null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(sqlOutputPath))!);
            File.WriteAllText(sqlOutputPath, sql);
            Console.WriteLine($"Generated Oracle 19c SQL: {sqlOutputPath}");
        }
        Console.WriteLine("Oracle 19c context, schema isolation, migration snapshot and SQL checks passed; no database connection opened.");
    }

    private static void CheckNumericBooleans(PlatformIdentityDbContext db) =>
        Check.That(db.Model.GetEntityTypes().SelectMany(entity => entity.GetProperties())
            .Where(property => property.ClrType == typeof(bool))
            .All(property => property.GetColumnType() == "NUMBER(1)"),
            "Every environment must map Boolean columns to NUMBER(1), including newer local Oracle versions.");
}
