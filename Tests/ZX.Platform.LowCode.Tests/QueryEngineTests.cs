using ZX.Platform.LowCode.Application.Metadata;
using ZX.Platform.LowCode.Application.Runtime;
using ZX.Platform.LowCode.Domain;
using ZX.Platform.LowCode.Domain.Metadata;
using ZX.Platform.LowCode.Infrastructure.Schema;

namespace ZX.Platform.LowCode.Tests;

public sealed class QueryEngineTests
{
    private const string AppCode = "mes";
    private const string EntityCode = "station";

    private static readonly CancellationToken Ct = CancellationToken.None;

    [Fact]
    public async Task 查询使用已发布定义并把值规范化()
    {
        var (engine, gateway) = await PublishedStationAsync();

        gateway.Result = new RecordPage(
            [
                new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["ID"] = Guid.NewGuid(),
                    ["CODE"] = "ST001",
                    ["NAME"] = "一站",
                    ["IS_ENABLED"] = 1m
                }
            ],
            Total: 1,
            Skip: 0,
            Take: 20);

        var page = await engine.QueryAsync(AppCode, EntityCode, new RecordQueryRequest(), RecordScope.Unscoped(null), Ct);

        var row = Assert.Single(page.Rows);
        Assert.Equal("ST001", row["code"]);
        Assert.Equal("一站", row["name"]);
        Assert.Equal(true, row["is_enabled"]);   // NUMBER(1) → bool
        Assert.True(row.ContainsKey("id"));      // 保留列键小写
    }

    [Fact]
    public async Task 未发布的实体查不到数据()
    {
        var (engine, _) = await DraftOnlyStationAsync();

        var error = await Assert.ThrowsAsync<LowCodeException>(
            () => engine.QueryAsync(AppCode, EntityCode, new RecordQueryRequest(), RecordScope.Unscoped(null), Ct));

        Assert.Equal(LowCodeErrorCodes.NotFound, error.Code);
        Assert.Contains("尚未发布", error.Message);
    }

    [Fact]
    public async Task 数据范围来自会话而不是请求体()
    {
        var (engine, gateway) = await PublishedStationAsync();
        var sbuId = Guid.NewGuid();

        await engine.QueryAsync(AppCode, EntityCode, new RecordQueryRequest(), new RecordScope(null, sbuId, false), Ct);

        var spec = Assert.Single(gateway.Queries);
        Assert.Equal(sbuId, spec.SbuId);
        Assert.False(spec.AllSbus);
    }

    [Fact]
    public async Task find按主键查询并返回空表示不存在()
    {
        var (engine, gateway) = await PublishedStationAsync();
        var id = Guid.NewGuid();

        Assert.Null(await engine.FindAsync(AppCode, EntityCode, id, RecordScope.Unscoped(null), Ct));

        var spec = Assert.Single(gateway.Queries);
        Assert.Equal(id, spec.Id);
        Assert.Equal(1, spec.Take);

        gateway.Result = new RecordPage(
            [new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["ID"] = id, ["CODE"] = "ST001", ["NAME"] = "一站", ["IS_ENABLED"] = 0m
            }],
            1, 0, 1);

        var row = await engine.FindAsync(AppCode, EntityCode, id, RecordScope.Unscoped(null), Ct);
        Assert.Equal("ST001", row!["code"]);
        Assert.Equal(false, row!["is_enabled"]);
    }

    [Fact]
    public async Task 请求里的分页与筛选原样下传()
    {
        var (engine, gateway) = await PublishedStationAsync();

        await engine.QueryAsync(AppCode, EntityCode, new RecordQueryRequest
        {
            Fields = ["code"],
            Filters = [new FilterCondition("code", QueryOperator.Like, "ST")],
            Sorts = [new ViewSort { Field = "code", Descending = true }],
            Skip = 40,
            Take = 500
        }, RecordScope.Unscoped(null), Ct);

        var spec = Assert.Single(gateway.Queries);
        Assert.Equal(new[] { "code" }, spec.Fields!);
        Assert.Equal(40, spec.Skip);
        Assert.Equal(500, spec.Take);   // 截断在 SQL 生成器里做，引擎不改写请求
        Assert.Equal(QueryOperator.Like, spec.Filters[0].Operator);
        Assert.True(spec.Sorts[0].Descending);
    }

    // ── 辅助 ───────────────────────────────────────────────────────────

    private static async Task<(QueryEngine Engine, FakeRecordGateway Gateway)> PublishedStationAsync()
    {
        var (service, gateway) = await PrepareAsync(publish: true);
        return (new QueryEngine(service, gateway), gateway);
    }

    private static async Task<(QueryEngine Engine, FakeRecordGateway Gateway)> DraftOnlyStationAsync()
    {
        var (service, gateway) = await PrepareAsync(publish: false);
        return (new QueryEngine(service, gateway), gateway);
    }

    private static async Task<(MetadataService Service, FakeRecordGateway Gateway)> PrepareAsync(bool publish)
    {
        var store = new InMemoryMetadataStore();
        var inspector = new InMemorySchemaInspector();
        var service = new MetadataService(store, inspector, new SchemaChangePlanner());

        await service.CreateAsync(AppCode, TestEntity.Station(), null, Ct);
        if (!publish) return (service, new FakeRecordGateway());

        var blocked = await service.PublishAsync(AppCode, EntityCode, null, Ct);
        inspector.CreateTable("LC_MES_STATION", "CODE", "NAME", "IS_ENABLED");
        blocked.PendingChanges[0].Status = SchemaChangeStatus.Applied;
        var published = await service.PublishAsync(AppCode, EntityCode, null, Ct);
        Assert.True(published.Published);

        return (service, new FakeRecordGateway());
    }
}
