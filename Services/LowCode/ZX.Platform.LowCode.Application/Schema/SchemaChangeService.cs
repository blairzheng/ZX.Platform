using System.Text;
using ZX.Platform.LowCode.Application.Metadata;
using ZX.Platform.LowCode.Domain;
using ZX.Platform.LowCode.Domain.Metadata;

namespace ZX.Platform.LowCode.Application.Schema;

/// <summary>
/// 结构变更的四种动作：执行（测试环境）、导出（生产）、核对、放行。
///
/// 一条变更只有走到 <c>applied</c> 才算完成，发布闸门才打开。中间的 executing / exported /
/// failed 都算"没完成"——见 <see cref="IMetadataStore.ListPendingChangesAsync"/>。
///
/// 核对这一步不能省：DDL 是导给 DBA 执行的，平台不知道对方到底执行了几条、
/// 有没有顺手改过。唯一可信的判断是回库里看一眼。
/// </summary>
public sealed class SchemaChangeService(
    IMetadataStore store,
    ISchemaInspector inspector,
    ISchemaChangeExecutor executor,
    TimeProvider? clock = null)
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    public Task<IReadOnlyList<SchemaChangeRecord>> ListAsync(
        string entityCode, CancellationToken cancellationToken) =>
        store.ListPendingChangesAsync(entityCode, cancellationToken);

    /// <summary>测试环境：直接执行。执行成功后再核对一遍，核对不过也算失败。</summary>
    public async Task<SchemaChangeRecord> ExecuteAsync(
        Guid changeId, Guid? userId, CancellationToken cancellationToken)
    {
        var change = await RequireAsync(changeId, cancellationToken);

        // 已经完成的再点一次不应该重跑 DDL。
        if (change.Status == SchemaChangeStatus.Applied) return change;

        change.Status = SchemaChangeStatus.Executing;
        change.ErrorMessage = null;
        await store.SaveChangesAsync(cancellationToken);

        try
        {
            await executor.ExecuteAsync(Plan(change).Statements, cancellationToken);
        }
        catch (Exception ex)
        {
            change.Status = SchemaChangeStatus.Failed;
            change.ErrorMessage = ex.Message;
            await store.SaveChangesAsync(cancellationToken);
            throw;
        }

        change.ExecutedAtUtc = _clock.GetUtcNow();

        var result = await VerifyAsync(change, cancellationToken);
        if (!result.Verified)
        {
            change.Status = SchemaChangeStatus.Failed;
            change.ErrorMessage = result.Message;
            await store.SaveChangesAsync(cancellationToken);
        }

        return change;
    }

    /// <summary>
    /// 生产环境：把脚本导出去交给 DBA。状态置 exported，**仍然算未完成**，
    /// 发布依旧被挡住，直到 DBA 执行完、核对通过。
    /// </summary>
    public async Task<string> ExportAsync(Guid changeId, CancellationToken cancellationToken)
    {
        var change = await RequireAsync(changeId, cancellationToken);

        if (change.Status is SchemaChangeStatus.Pending or SchemaChangeStatus.Failed)
        {
            change.Status = SchemaChangeStatus.Exported;
            change.ErrorMessage = null;
            await store.SaveChangesAsync(cancellationToken);
        }

        return RenderScript(change);
    }

    /// <summary>
    /// 回库核对。通过就置 applied（放行发布），不通过只返回原因、不改状态。
    /// </summary>
    public async Task<VerifyResult> VerifyAsync(Guid changeId, CancellationToken cancellationToken) =>
        await VerifyAsync(await RequireAsync(changeId, cancellationToken), cancellationToken);

    private async Task<VerifyResult> VerifyAsync(
        SchemaChangeRecord change, CancellationToken cancellationToken)
    {
        var plan = Plan(change);
        var shape = await inspector.InspectAsync(plan.Table, cancellationToken);

        var missing = plan.Kind switch
        {
            SchemaChangeKind.CreateTable when !shape.Exists => $"表 {plan.Table} 还不存在。",
            SchemaChangeKind.AddColumn when !shape.Exists => $"表 {plan.Table} 还不存在。",
            SchemaChangeKind.AddColumn when !shape.HasColumn(plan.Field!.ToUpperInvariant())
                => $"表 {plan.Table} 里还没有列 {plan.Field!.ToUpperInvariant()}。",
            SchemaChangeKind.CreateTable or SchemaChangeKind.AddColumn => null,
            _ => $"不认识的变更类型 {plan.Kind}。"
        };

        if (missing is not null) return VerifyResult.Missing(missing);

        change.Status = SchemaChangeStatus.Applied;
        change.VerifiedAtUtc = _clock.GetUtcNow();
        change.ErrorMessage = null;
        await store.SaveChangesAsync(cancellationToken);
        return VerifyResult.Ok();
    }

    /// <summary>
    /// 导出文本。加一段注释头，DBA 能直接从脚本看出这条变更对应哪个实体；
    /// 每条语句后用 <c>;</c> 加空行，sqlplus 与各类客户端都能直接跑。
    /// </summary>
    private static string RenderScript(SchemaChangeRecord change)
    {
        var plan = Plan(change);
        var script = new StringBuilder()
            .Append("-- ZX.Platform 低代码结构变更\n")
            .Append($"-- change: {change.Id}\n")
            .Append($"-- app: {plan.AppCode}  entity: {plan.Entity}  kind: {plan.Kind}\n")
            .Append($"-- table: {plan.Table}\n");

        if (!string.IsNullOrWhiteSpace(plan.Field)) script.Append($"-- field: {plan.Field}\n");
        script.Append('\n');

        foreach (var statement in plan.Statements) script.Append(statement).Append(";\n\n");

        return script.ToString();
    }

    private static SchemaChangePlan Plan(SchemaChangeRecord change) =>
        MetadataJson.Deserialize<SchemaChangePlan>(change.PlanJson);

    private async Task<SchemaChangeRecord> RequireAsync(Guid changeId, CancellationToken cancellationToken)
    {
        var change = await store.FindChangeAsync(changeId, cancellationToken);
        return change ?? throw new LowCodeException(
            LowCodeErrorCodes.NotFound, $"结构变更 {changeId} 不存在。");
    }
}
