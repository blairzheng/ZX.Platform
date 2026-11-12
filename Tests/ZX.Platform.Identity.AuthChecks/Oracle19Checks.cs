using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Text.RegularExpressions;
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
        CheckDatabaseCreationTime(db);
        CheckHistorySql(db, "ZX_IDENTITY");
        var migrations = db.Database.GetMigrations().ToArray();
        Check.That(migrations.Length >= 2 && migrations[0] == "20261027000000_InitialIdentitySchema" &&
            migrations.Contains("20261028084818_ReplaceFactoriesWithSbus") &&
            migrations[^1] == "20261112062603_UseScalarOaDirectoryPaths",
            "The project must have one Oracle 19c migration chain starting at its new baseline and ending at the current migration.");

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

        // OA 登录迁移：新增账号状态列、可空密码、唯一身份索引和校验约束。
        var approvalSql = migrator.GenerateScript(
            fromMigration: "20261030100000_AddMenuLauncherMetadata",
            toMigration: "20261102111516_AddLdapAccountApproval");
        Check.That(approvalSql.Contains("\"AUTH_TYPE\"") && approvalSql.Contains("\"APPROVAL_STATUS\"") &&
            approvalSql.Contains("\"SECURITY_VERSION\"") && approvalSql.Contains("\"EMPLOYEE_NO\"") &&
            approvalSql.Contains("UX_ZID_USER_EMPLOYEE_NO") && approvalSql.Contains("UX_ZID_USER_LDAP_IDENTITY") &&
            approvalSql.Contains("CK_ZID_USER_PASSWORD"),
            "The LDAP approval migration must add account state columns, identity indexes and password constraints.");
        Check.That(!approvalSql.Contains("WHERE \"EMPLOYEE_NO\"", StringComparison.Ordinal) &&
            !approvalSql.Contains("WHERE \"LDAP_PROVIDER\"", StringComparison.Ordinal),
            "Oracle does not support filtered indexes; unique identity indexes must rely on NULL semantics.");

        // 审批审计迁移：只追加表与索引，不改动既有用户数据、不混入业务逻辑。
        var auditSql = migrator.GenerateScript(
            fromMigration: "20261102111516_AddLdapAccountApproval",
            toMigration: "20261102144318_AddUserApprovalAudit");
        Check.That(auditSql.Contains("\"ZX_IDENTITY\".\"ZX_IDENTITY_USER_APPROVAL_AUDIT\"") &&
            auditSql.Contains("\"ACTION\"") && auditSql.Contains("\"FROM_STATUS\"") &&
            auditSql.Contains("\"TO_STATUS\"") && auditSql.Contains("\"TRACE_ID\"") &&
            auditSql.Contains("\"ACTOR_USER_ID\""),
            "The approval audit migration must persist actor, action, before/after status and trace id.");
        Check.That(!auditSql.Contains("INSERT INTO \"ZX_IDENTITY\".\"ZX_IDENTITY_USER\"", StringComparison.Ordinal) &&
            !auditSql.Contains("UPDATE \"ZX_IDENTITY\".\"ZX_IDENTITY_USER\"", StringComparison.Ordinal) &&
            !auditSql.Contains("DELETE FROM \"ZX_IDENTITY\".\"ZX_IDENTITY_USER\"", StringComparison.Ordinal),
            "The audit migration must not rewrite existing user rows.");

        // 审计快照容量迁移：Oracle 不能直接 MODIFY 成 CLOB，必须新增列后复制、删旧列再改名；
        // 复制时不能截断，回退前要先确认放得下 2000 字符。
        var widenSql = migrator.GenerateScript(
            fromMigration: "20261102144318_AddUserApprovalAudit",
            toMigration: "20261102150741_WidenApprovalAuditSnapshot");
        Check.That(widenSql.Contains("add (\"ROLE_IDS_CLOB\" CLOB)") &&
            widenSql.Contains("add (\"SBU_IDS_CLOB\" CLOB)") &&
            widenSql.Contains("set \"ROLE_IDS_CLOB\" = to_clob(\"ROLE_IDS\")") &&
            widenSql.Contains("rename column \"ROLE_IDS_CLOB\" to \"ROLE_IDS\""),
            "Audit snapshot columns must be widened to CLOB by copy-and-rename so no evidence is lost.");
        Check.That(!widenSql.Contains("MODIFY \"ROLE_IDS\"", StringComparison.Ordinal) &&
            !widenSql.Contains("MODIFY \"SBU_IDS\"", StringComparison.Ordinal),
            "Oracle rejects MODIFY to CLOB with ORA-22858; the migration must not emit it.");
        Check.That(!widenSql.Contains("substr", StringComparison.OrdinalIgnoreCase) &&
            !widenSql.Contains("DELETE FROM \"ZX_IDENTITY\".\"ZX_IDENTITY_USER_APPROVAL_AUDIT\"", StringComparison.Ordinal),
            "Widening the audit snapshot must keep existing evidence intact instead of truncating or deleting it.");

        var pendingLoginSql = migrator.GenerateScript(
            fromMigration: "20261102150741_WidenApprovalAuditSnapshot",
            toMigration: "20261103090000_AllowPendingLdapLogin");
        Check.That(pendingLoginSql.Contains("DROP CONSTRAINT \"CK_ZID_USER_LDAP_ENABLED\"") &&
            pendingLoginSql.Contains("APPROVAL_STATUS <> 'Rejected'") &&
            !pendingLoginSql.Contains("UPDATE ", StringComparison.OrdinalIgnoreCase) &&
            !pendingLoginSql.Contains("DELETE FROM", StringComparison.OrdinalIgnoreCase),
            "Pending login migration must only relax the constraint, without enabling users or modifying grants.");
        var pendingRollbackSql = migrator.GenerateScript(
            fromMigration: "20261103090000_AllowPendingLdapLogin",
            toMigration: "20261102150741_WidenApprovalAuditSnapshot");
        Check.That(pendingRollbackSql.Contains("raise_application_error(-20011") &&
            pendingRollbackSql.IndexOf("raise_application_error", StringComparison.Ordinal) <
                pendingRollbackSql.IndexOf("DROP CONSTRAINT", StringComparison.Ordinal),
            "Rollback must check enabled Pending accounts before any implicitly committed DDL.");

        // 越南语菜单迁移：只给菜单表追加两个可空文本列，不改动菜单与授权数据。
        var vietnameseSql = migrator.GenerateScript(
            fromMigration: "20261109012525_AddDatabaseCreationTime",
            toMigration: "20261111000917_AddVietnameseMenuTranslations");
        Check.That(vietnameseSql.Contains("\"TITLE_VI\"") &&
            vietnameseSql.Contains("\"LAUNCHER_DESCRIPTION_VI\"") &&
            vietnameseSql.Contains("NVARCHAR2(200)") && vietnameseSql.Contains("NVARCHAR2(500)") &&
            !vietnameseSql.Contains("UPDATE ", StringComparison.OrdinalIgnoreCase) &&
            !vietnameseSql.Contains("DELETE FROM", StringComparison.OrdinalIgnoreCase),
            "The Vietnamese menu migration must only add the two nullable text columns without touching menu or grant data.");

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
            CheckDatabaseCreationTime(local);
            CheckHistorySql(local, schema ?? "ZX_IDENTITY");
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

    private static void CheckDatabaseCreationTime(PlatformIdentityDbContext db)
    {
        foreach (var entity in db.Model.GetEntityTypes())
        {
            var createTime = entity.FindProperty("DatabaseCreateTime");
            Check.That(createTime is not null && createTime.IsShadowProperty() &&
                createTime.GetColumnName() == "CREATE_TIME" && createTime.GetColumnType() == "DATE" &&
                createTime.GetDefaultValueSql() == "SYSDATE" && !createTime.IsNullable &&
                createTime.ValueGenerated == ValueGenerated.OnAdd &&
                createTime.GetBeforeSaveBehavior() == PropertySaveBehavior.Ignore &&
                createTime.GetAfterSaveBehavior() == PropertySaveBehavior.Ignore,
                $"{entity.GetTableName()} must let Oracle own its creation time independently of UTC audit fields.");
        }
    }

    internal static void CheckHistorySql(PlatformIdentityDbContext db, string schema)
    {
        var history = db.GetService<IHistoryRepository>();
        Check.That(history is PlatformIdentityHistoryRepository,
            "Runtime and design-time contexts must use the shared Oracle migration-history mapping.");
        var table = $"\"{schema}\".\"{PlatformIdentityDbContext.HistoryTable}\"";
        var createSql = history.GetCreateScript();
        Check.That(createSql.Contains(table) && createSql.Contains("\"MIGRATION_ID\"") &&
            createSql.Contains("\"PRODUCT_VERSION\"") &&
            Regex.IsMatch(createSql, "\"CREATE_TIME\"\\s+DATE\\s+DEFAULT\\s+\\(?SYSDATE\\)?\\s+NOT\\s+NULL",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            "History DDL must use the configured owner, uppercase columns and a database-generated creation time.");

        const string migration = "20261109000000_HistorySqlCheck";
        var insertSql = history.GetInsertScript(new HistoryRow(migration, "10.0.12"));
        var statements = new[]
        {
            createSql,
            insertSql,
            history.GetDeleteScript(migration),
            history.GetBeginIfExistsScript(migration),
            history.GetBeginIfNotExistsScript(migration)
        };
        Check.That(statements.All(statement => statement.Contains(table) &&
                statement.Contains("\"MIGRATION_ID\"") &&
                !statement.Contains("\"MigrationId\"") && !statement.Contains("\"ProductVersion\"")) &&
            insertSql.Contains("\"PRODUCT_VERSION\"") && !insertSql.Contains("\"CREATE_TIME\""),
            "Migration inserts, deletes and idempotency checks must agree on uppercase history columns and let CREATE_TIME default.");
    }
}
