using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using ZX.Platform.Identity.Api.Persistence;

namespace ZX.Platform.Identity.AuthChecks;

internal static class DevelopmentMigrationChecks
{
    public static void Run()
    {
        var factory = new PlatformIdentityDesignTimeFactory();
        using (var offline = factory.CreateDbContext([]))
        {
            Check.That(offline.Database.GetDbConnection().DataSource.Contains("127.0.0.1:1"),
                "Default migration generation must retain its offline connection.");
            Check.That(!offline.Database.HasPendingModelChanges(),
                "Moving the design-time factory must not change the unified snapshot.");
        }

        foreach (var schema in new[] { "ZX_IDENTITY", "TEAM_TEST" })
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:IdentityDatabase"] =
                        "User Id=TEST_ONLY;Password=unused;Data Source=127.0.0.1:1/TEST_ONLY",
                    ["IdentityDatabase:Schema"] = schema,
                }).Build();
            using var db = PlatformIdentityDesignTimeFactory.CreateDevelopmentContext(configuration);
            Check.That(!db.Database.HasPendingModelChanges(),
                "A personal test schema must not introduce pending model changes.");
            Check.That(db.Model.GetEntityTypes().All(entity => entity.GetSchema() == "ZX_IDENTITY"),
                "Development configuration must not leak into the canonical model.");
            Oracle19Checks.CheckHistorySql(db, schema);

            var sql = db.GetService<IMigrator>().GenerateScript();
            Check.That(sql.Contains($"\"{schema}\".\"ZX_IDENTITY_USER\"") &&
                sql.Contains($"\"{schema}\".\"ZX_IDENTITY_MIGRATIONS\""),
                "Both business objects and history must target the configured test owner.");
            Check.That(sql.Contains($"OWNER = '{schema}'") &&
                sql.Contains($"alter table \"{schema}\".\"ZX_IDENTITY_USER_APPROVAL_AUDIT\""),
                "Provider owner literals and hand-written migration SQL must also be mapped.");
            if (schema != "ZX_IDENTITY")
                Check.That(!sql.Contains("\"ZX_IDENTITY\".") && !sql.Contains("'ZX_IDENTITY'"),
                    "Development SQL must not retain source-schema references.");
        }

        ExpectInvalid(() => factory.CreateDbContext(["--environment", "Production"]));
        ExpectInvalid(() => PlatformIdentityDesignTimeFactory.CreateDevelopmentContext(
            new ConfigurationBuilder().Build()));
        ExpectInvalid(() => PlatformIdentityDesignTimeFactory.CreateDevelopmentContext(
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:IdentityDatabase"] = "User Id=TEST_ONLY;Password=unused;Data Source=127.0.0.1:1/TEST_ONLY",
                ["IdentityDatabase:Schema"] = "BAD.SCHEMA",
            }).Build()));
        Console.WriteLine("Development migration checks passed (offline; no database changes).");
    }

    private static void ExpectInvalid(Func<IDisposable> action)
    {
        try { using var ignored = action(); }
        catch (InvalidOperationException) { return; }
        throw new InvalidOperationException("Invalid development migration configuration must be rejected.");
    }
}
