using ZX.Platform.LowCode.Application.Metadata;
using ZX.Platform.LowCode.Application.Schema;
using ZX.Platform.LowCode.Domain;
using ZX.Platform.LowCode.Domain.Metadata;
using ZX.Platform.LowCode.Infrastructure.Schema;

namespace ZX.Platform.LowCode.Tests;

/// <summary>
/// 结构变更的状态机。核心断言只有一条：**没核对通过之前，发布必须一直被挡住**。
/// </summary>
public sealed class SchemaChangeServiceTests
{
    private const string AppCode = "mes";
    private const string EntityCode = "station";
    private const string TableName = "LC_MES_STATION";

    private static readonly CancellationToken Ct = CancellationToken.None;

    [Fact]
    public async Task 执行成功后核对通过并放行发布()
    {
        var h = await BlockedAsync();

        await h.Service.ExecuteAsync(h.Change.Id, null, Ct);

        Assert.Equal(SchemaChangeStatus.Applied, h.Change.Status);
        Assert.NotNull(h.Change.ExecutedAtUtc);
        Assert.NotNull(h.Change.VerifiedAtUtc);

        var published = await h.Metadata.PublishAsync(AppCode, EntityCode, null, Ct);
        Assert.True(published.Published);
    }

    [Fact]
    public async Task 执行失败记下原因且不放行()
    {
        var h = await BlockedAsync();
        h.Executor.FailAt = 1;   // 第 2 条语句失败

        var error = await Assert.ThrowsAsync<LowCodeException>(
            () => h.Service.ExecuteAsync(h.Change.Id, null, Ct));

        Assert.Equal(LowCodeErrorCodes.StateInvalid, error.Code);
        Assert.Equal(SchemaChangeStatus.Failed, h.Change.Status);
        Assert.Contains("模拟第 2 条失败", h.Change.ErrorMessage);

        // 失败也是一种"没完成"，发布照样被挡。
        var blocked = await h.Metadata.PublishAsync(AppCode, EntityCode, null, Ct);
        Assert.False(blocked.Published);
    }

    [Fact]
    public async Task 导出之后仍然挡住发布()
    {
        // 这是最容易漏的一条：变更从 pending 走到 exported，如果"待执行"只看 pending，
        // 闸门就失效了，元数据先激活、表还没建，运行时立刻 500。
        var h = await BlockedAsync();

        var script = await h.Service.ExportAsync(h.Change.Id, Ct);

        Assert.Equal(SchemaChangeStatus.Exported, h.Change.Status);
        Assert.Contains("CREATE TABLE", script);
        Assert.Contains("entity: " + EntityCode, script);
        Assert.Contains(";\n", script);

        var blocked = await h.Metadata.PublishAsync(AppCode, EntityCode, null, Ct);
        Assert.False(blocked.Published);
        Assert.Single(blocked.PendingChanges);
    }

    [Fact]
    public async Task DBA执行完导出脚本后核对通过()
    {
        var h = await BlockedAsync();
        await h.Service.ExportAsync(h.Change.Id, Ct);

        // 模拟 DBA 在库里执行了导出脚本。
        h.CreateTableInDb();

        var result = await h.Service.VerifyAsync(h.Change.Id, Ct);

        Assert.True(result.Verified);
        Assert.Equal(SchemaChangeStatus.Applied, h.Change.Status);

        var published = await h.Metadata.PublishAsync(AppCode, EntityCode, null, Ct);
        Assert.True(published.Published);
    }

    [Fact]
    public async Task 库里没有变化时核对不通过且不改状态()
    {
        var h = await BlockedAsync();
        await h.Service.ExportAsync(h.Change.Id, Ct);

        var result = await h.Service.VerifyAsync(h.Change.Id, Ct);

        Assert.False(result.Verified);
        Assert.Contains(TableName, result.Message);
        Assert.Equal(SchemaChangeStatus.Exported, h.Change.Status);
    }

    [Fact]
    public async Task 已完成的变更重复执行不再跑DDL()
    {
        var h = await BlockedAsync();
        await h.Service.ExecuteAsync(h.Change.Id, null, Ct);
        var runs = h.Executor.Calls;

        await h.Service.ExecuteAsync(h.Change.Id, null, Ct);

        Assert.Equal(runs, h.Executor.Calls);
    }

    [Fact]
    public async Task 加列的变更核对的是列不是表()
    {
        var h = await BlockedAsync();
        await h.Service.ExecuteAsync(h.Change.Id, null, Ct);
        await h.Metadata.PublishAsync(AppCode, EntityCode, null, Ct);

        // 发布后再加一个字段。
        await h.Metadata.SaveDraftAsync(AppCode, EntityCode, TestEntity.StationWith(new FieldDefinition
        {
            Key = "sort_no",
            Type = FieldType.Number,
            Name = "排序号",
            Sort = 40
        }), revision: 1, null, Ct);

        var blocked = await h.Metadata.PublishAsync(AppCode, EntityCode, null, Ct);
        var change = Assert.Single(blocked.PendingChanges);
        Assert.Equal(SchemaChangeKind.AddColumn, change.Kind);

        // 表在、列不在 → 核对必须不通过。
        var notYet = await h.Service.VerifyAsync(change.Id, Ct);
        Assert.False(notYet.Verified);
        Assert.Contains("SORT_NO", notYet.Message);

        h.Inspector.AddColumn(TableName, "SORT_NO");

        var ok = await h.Service.VerifyAsync(change.Id, Ct);
        Assert.True(ok.Verified);
    }

    [Fact]
    public async Task 变更不存在报600002()
    {
        var h = await BlockedAsync();

        var error = await Assert.ThrowsAsync<LowCodeException>(
            () => h.Service.VerifyAsync(Guid.NewGuid(), Ct));

        Assert.Equal(LowCodeErrorCodes.NotFound, error.Code);
    }

    // ── 辅助 ───────────────────────────────────────────────────────────

    /// <summary>造一个"实体已建草稿、第一次发布被建表变更挡住"的状态。</summary>
    private static async Task<Harness> BlockedAsync()
    {
        var store = new InMemoryMetadataStore();
        var inspector = new InMemorySchemaInspector();
        var planner = new SchemaChangePlanner();
        var metadata = new MetadataService(store, inspector, planner);
        var executor = new FakeSchemaChangeExecutor(inspector);
        var service = new SchemaChangeService(store, inspector, executor);

        await metadata.CreateAsync(AppCode, TestEntity.Station(), null, Ct);
        var blocked = await metadata.PublishAsync(AppCode, EntityCode, null, Ct);
        var change = Assert.Single(blocked.PendingChanges);

        return new Harness(metadata, inspector, service, executor, change);
    }

    private sealed record Harness(
        MetadataService Metadata,
        InMemorySchemaInspector Inspector,
        SchemaChangeService Service,
        FakeSchemaChangeExecutor Executor,
        SchemaChangeRecord Change)
    {
        public void CreateTableInDb() => Inspector.CreateTable(TableName, "CODE", "NAME", "IS_ENABLED");
    }
}

/// <summary>
/// 假的 DDL 执行器。它把"执行"这件事拆成两步：记录语句 + 让调用方决定库变成什么样，
/// 这样才能测出"执行成功但核对不通过"这种真实存在的分支。
/// </summary>
internal sealed class FakeSchemaChangeExecutor(InMemorySchemaInspector inspector) : ISchemaChangeExecutor
{
    private const string TableName = "LC_MES_STATION";

    public int Calls { get; private set; }

    public List<IReadOnlyList<string>> Executed { get; } = [];

    /// <summary>第几条语句失败（从 0 开始）。null 表示全部成功。</summary>
    public int? FailAt { get; set; }

    public Task ExecuteAsync(IReadOnlyList<string> statements, CancellationToken cancellationToken)
    {
        Calls++;
        Executed.Add(statements);

        for (var index = 0; index < statements.Count; index++)
        {
            if (FailAt == index)
                throw new LowCodeException(LowCodeErrorCodes.StateInvalid, $"模拟第 {index + 1} 条失败");
        }

        // 默认行为：按语句内容让库"长出来"，模拟真实执行成功。
        if (FailAt is null) Apply(statements);
        return Task.CompletedTask;
    }

    private void Apply(IReadOnlyList<string> statements)
    {
        foreach (var statement in statements)
        {
            if (statement.StartsWith("CREATE TABLE", StringComparison.Ordinal))
            {
                inspector.CreateTable(TableName, "CODE", "NAME", "IS_ENABLED");
                continue;
            }

            // ALTER TABLE "T" ADD ("COL" ...) → 只取第一个加出来的列
            if (!statement.StartsWith("ALTER TABLE", StringComparison.Ordinal)) continue;

            var start = statement.IndexOf("ADD (\"", StringComparison.Ordinal);
            if (start < 0) continue;

            start += "ADD (\"".Length;
            var end = statement.IndexOf('"', start);
            if (end < 0) continue;

            inspector.AddColumn(TableName, statement[start..end]);
        }
    }
}
