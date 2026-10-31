using ZX.Platform.LowCode.Application.Metadata;
using ZX.Platform.LowCode.Domain;
using ZX.Platform.LowCode.Domain.Metadata;
using ZX.Platform.LowCode.Infrastructure.Oracle;

namespace ZX.Platform.LowCode.Infrastructure.Schema;

/// <summary>
/// 把"草稿要求的样子"和"库里现在的样子"的差异翻译成 DDL。
///
/// 只做两种变更：建表、加列。改类型由 SchemaCompatibility 在更上层挡掉，
/// 删列永远不做——物理列一旦删掉，历史数据就没了，而元数据里的字段只是"停用"。
///
/// 幂等性靠"问库"保证：已经存在的列不重复生成 ALTER，
/// 所以这个规划器可以安全地重复执行，也可以接受 DBA 手工提前建好的列。
/// </summary>
public sealed class SchemaChangePlanner(string? schema = null) : ISchemaChangePlanner
{
    public IReadOnlyList<PlannedChange> Plan(EntityDefinition draft, TableShape shape)
    {
        // 表还不存在：第一次发布，产出建表 + 索引。
        if (!shape.Exists)
        {
            var createTable = DdlSqlBuilder.CreateTable(draft, schema);
            return
            [
                new PlannedChange(
                    SchemaChangeKind.CreateTable,
                    createTable,
                    PlanJson(SchemaChangeKind.CreateTable, draft, null, createTable))
            ];
        }

        var changes = new List<PlannedChange>();

        foreach (var field in draft.ActiveFields.OrderBy(item => item.Sort))
        {
            if (shape.HasColumn(field.ColumnName)) continue;

            // 表里已经有行，NOT NULL 且无默认值的列加不上去。
            // 这条限制无法在校验器里表达：只有这里知道表已经存在。
            if (field.Required && !field.HasDefault)
                throw new LowCodeException(LowCodeErrorCodes.ValidationFailed,
                    $"新增字段「{field.Key}」必须可空或提供 defaultValue：表里已有数据，"
                    + "无法直接加 NOT NULL 列。");

            var statements = DdlSqlBuilder.AddColumn(draft, schema, field);
            changes.Add(new PlannedChange(
                SchemaChangeKind.AddColumn,
                statements,
                PlanJson(SchemaChangeKind.AddColumn, draft, field.Key, statements)));
        }

        return changes;
    }

    private static string PlanJson(
        string kind, EntityDefinition entity, string? fieldKey, IReadOnlyList<string> statements) =>
        MetadataJson.Serialize(new
        {
            kind,
            appCode = entity.AppCode,
            entity = entity.Code,
            field = fieldKey,
            table = entity.TableName,
            // 语句列表也进 PlanJson：ScriptSql 只是它的渲染结果，
            // 执行/导出/核对都读同一份，不允许"从 SQL 文本反推计划"。
            statements
        });
}
