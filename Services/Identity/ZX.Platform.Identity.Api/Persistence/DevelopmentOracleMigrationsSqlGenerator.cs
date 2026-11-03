using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Oracle.EntityFrameworkCore.Infrastructure.Internal;
using Oracle.EntityFrameworkCore.Migrations;

namespace ZX.Platform.Identity.Api.Persistence;

// Oracle 的生成器构造参数使用 provider 内部接口；随 provider 升级由离线迁移检查验证兼容性。
#pragma warning disable EF1001
/// <summary>仅开发库入口启用：将统一源 schema 转为测试库 owner，包含手写 SQL。</summary>
public sealed class DevelopmentOracleMigrationsSqlGenerator(
    MigrationsSqlGeneratorDependencies dependencies,
    IOracleOptions oracleOptions,
    IDbContextOptions options) : OracleMigrationsSqlGenerator(dependencies, oracleOptions)
{
    public override IReadOnlyList<MigrationCommand> Generate(
        IReadOnlyList<MigrationOperation> operations,
        IModel? model = null,
        MigrationsSqlGenerationOptions options = MigrationsSqlGenerationOptions.Default)
    {
        var commands = base.Generate(operations, model, options);
        var schema = GetTargetSchema();
        if (schema == "ZX_IDENTITY") return commands;

        var builder = new MigrationCommandListBuilder(Dependencies);
        foreach (var command in commands)
        {
            // 与现有发布导出工具采用同一替换规则；不修改迁移、Designer 和 snapshot。
            var sql = command.CommandText.Replace("\"ZX_IDENTITY\"", $"\"{schema}\"", StringComparison.Ordinal)
                .Replace("'ZX_IDENTITY'", $"'{schema}'", StringComparison.Ordinal);
            builder.Append(sql).EndCommand(command.TransactionSuppressed);
        }
        return builder.GetCommandList();
    }

    private string GetTargetSchema() => options.Extensions.OfType<RelationalOptionsExtension>()
        .Single().MigrationsHistoryTableSchema
        ?? throw new InvalidOperationException("开发库迁移未配置目标 schema。");
}
#pragma warning restore EF1001
