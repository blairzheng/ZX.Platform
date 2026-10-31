using ZX.Platform.LowCode.Application.Metadata;
using ZX.Platform.LowCode.Application.Runtime;
using ZX.Platform.LowCode.Domain;
using ZX.Platform.LowCode.Domain.Metadata;
using ZX.Platform.LowCode.Infrastructure.Schema;

namespace ZX.Platform.LowCode.Tests;

public sealed class RecordEngineTests
{
    private const string AppCode = "mes";
    private const string EntityCode = "station";

    private static readonly CancellationToken Ct = CancellationToken.None;
    private static readonly DateTimeOffset Now = new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid UserId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task 新增返回新主键并把归属与会话一起下传()
    {
        var (engine, gateway) = await PublishedStationAsync();
        var sbuId = Guid.NewGuid();

        var id = await engine.CreateAsync(
            AppCode,
            EntityCode,
            new Dictionary<string, object?> { ["code"] = "ST001", ["name"] = "一站" },
            new RecordScope(UserId, sbuId, false),
            Ct);

        var created = Assert.Single(gateway.Creates);
        Assert.Equal(id, created.Id);
        Assert.NotEqual(Guid.Empty, id);
        Assert.Equal(sbuId, created.Scope.SbuId);
        Assert.Equal(UserId, created.Scope.UserId);
        Assert.Equal(Now, created.Now);
    }

    [Fact]
    public async Task 新增拒绝未知字段()
    {
        var (engine, gateway) = await PublishedStationAsync();

        var error = await Assert.ThrowsAsync<LowCodeException>(() => engine.CreateAsync(
            AppCode, EntityCode,
            new Dictionary<string, object?> { ["code"] = "ST001", ["nmae"] = "拼错了" },
            RecordScope.Unscoped(UserId), Ct));

        Assert.Equal(LowCodeErrorCodes.ValidationFailed, error.Code);
        Assert.Contains("nmae", error.Message);
        Assert.Empty(gateway.Creates);   // 校验不过，一行都没写
    }

    [Fact]
    public async Task 新增拒绝已停用字段()
    {
        var (engine, gateway) = await PublishedStationAsync(extra:
        [
            new FieldDefinition { Key = "legacy", Type = FieldType.Text, Name = "旧字段", Disabled = true }
        ]);

        var error = await Assert.ThrowsAsync<LowCodeException>(() => engine.CreateAsync(
            AppCode, EntityCode,
            new Dictionary<string, object?> { ["code"] = "ST001", ["legacy"] = "x" },
            RecordScope.Unscoped(UserId), Ct));

        Assert.Equal(LowCodeErrorCodes.ValidationFailed, error.Code);
        Assert.Contains("已停用", error.Message);
        Assert.Empty(gateway.Creates);
    }

    [Fact]
    public async Task 新增时客户端传的sbu_id不生效也不报错()
    {
        var (engine, gateway) = await PublishedStationAsync();
        var sbuId = Guid.NewGuid();

        await engine.CreateAsync(
            AppCode, EntityCode,
            new Dictionary<string, object?>
            {
                ["code"] = "ST001",
                ["sbu_id"] = Guid.NewGuid()   // 伪造的归属
            },
            new RecordScope(UserId, sbuId, false), Ct);

        // 归属只认会话，请求体里的那个值根本不参与。
        Assert.Equal(sbuId, Assert.Single(gateway.Creates).Scope.SbuId);
    }

    [Fact]
    public async Task 唯一字段重复时新增报600003()
    {
        var (engine, gateway) = await PublishedStationAsync();
        gateway.Result = RowWith(Guid.NewGuid());

        var error = await Assert.ThrowsAsync<LowCodeException>(() => engine.CreateAsync(
            AppCode, EntityCode,
            new Dictionary<string, object?> { ["code"] = "ST001" },
            RecordScope.Unscoped(UserId), Ct));

        Assert.Equal(LowCodeErrorCodes.Conflict, error.Code);
        Assert.Contains("ST001", error.Message);
        Assert.Empty(gateway.Creates);
    }

    [Fact]
    public async Task 唯一性预检不按SBU过滤()
    {
        // 唯一索引建在整张表上。预检若只看当前 SBU，两个 SBU 各插一条同编码会双双放行。
        var (engine, gateway) = await PublishedStationAsync();

        await engine.CreateAsync(
            AppCode, EntityCode,
            new Dictionary<string, object?> { ["code"] = "ST001" },
            new RecordScope(UserId, Guid.NewGuid(), false), Ct);

        var spec = Assert.Single(gateway.Queries);
        Assert.True(spec.AllSbus);
        Assert.Null(spec.SbuId);
        Assert.Equal("code", spec.Filters[0].Field);
    }

    [Fact]
    public async Task 修改把revision下传用于乐观锁()
    {
        var (engine, gateway) = await PublishedStationAsync();
        var id = Guid.NewGuid();

        await engine.UpdateAsync(
            AppCode, EntityCode, id, revision: 7,
            new Dictionary<string, object?> { ["name"] = "二站" },
            RecordScope.Unscoped(UserId), Ct);

        var updated = Assert.Single(gateway.Updates);
        Assert.Equal(id, updated.Id);
        Assert.Equal(7, updated.Revision);
        Assert.Equal("二站", updated.Values["name"]);
    }

    [Fact]
    public async Task 修改时唯一冲突要排除自身()
    {
        var (engine, gateway) = await PublishedStationAsync();
        var id = Guid.NewGuid();
        gateway.Result = RowWith(id);   // 查到的就是自己

        await engine.UpdateAsync(
            AppCode, EntityCode, id, revision: 1,
            new Dictionary<string, object?> { ["code"] = "ST001" },
            RecordScope.Unscoped(UserId), Ct);

        Assert.Single(gateway.Updates);
    }

    [Fact]
    public async Task 修改影响0行报600003()
    {
        var (engine, gateway) = await PublishedStationAsync();
        gateway.WriteSucceeds = false;

        var error = await Assert.ThrowsAsync<LowCodeException>(() => engine.UpdateAsync(
            AppCode, EntityCode, Guid.NewGuid(), revision: 3,
            new Dictionary<string, object?> { ["name"] = "二站" },
            RecordScope.Unscoped(UserId), Ct));

        Assert.Equal(LowCodeErrorCodes.Conflict, error.Code);
        Assert.Contains("刷新", error.Message);
    }

    [Fact]
    public async Task 删除影响0行报600003()
    {
        var (engine, gateway) = await PublishedStationAsync();
        gateway.WriteSucceeds = false;

        var error = await Assert.ThrowsAsync<LowCodeException>(() => engine.DeleteAsync(
            AppCode, EntityCode, Guid.NewGuid(), revision: 3,
            RecordScope.Unscoped(UserId), Ct));

        Assert.Equal(LowCodeErrorCodes.Conflict, error.Code);
    }

    [Fact]
    public async Task 删除带revision且不动物理行()
    {
        var (engine, gateway) = await PublishedStationAsync();
        var id = Guid.NewGuid();

        await engine.DeleteAsync(AppCode, EntityCode, id, revision: 4, RecordScope.Unscoped(UserId), Ct);

        var deleted = Assert.Single(gateway.Deletes);
        Assert.Equal(id, deleted.Id);
        Assert.Equal(4, deleted.Revision);
        Assert.Empty(gateway.Updates);
    }

    [Fact]
    public async Task 未发布的实体写不进去()
    {
        var (engine, gateway) = await DraftOnlyStationAsync();

        var error = await Assert.ThrowsAsync<LowCodeException>(() => engine.CreateAsync(
            AppCode, EntityCode,
            new Dictionary<string, object?> { ["code"] = "ST001" },
            RecordScope.Unscoped(UserId), Ct));

        Assert.Equal(LowCodeErrorCodes.NotFound, error.Code);
        Assert.Empty(gateway.Creates);
    }

    // ── 辅助 ───────────────────────────────────────────────────────────

    private static RecordPage RowWith(Guid id) => new(
        [
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                [ReservedColumns.Id] = id,
                ["CODE"] = "ST001"
            }
        ],
        Total: 1,
        Skip: 0,
        Take: 1);

    private static async Task<(RecordEngine Engine, FakeRecordGateway Gateway)> PublishedStationAsync(
        FieldDefinition[]? extra = null)
    {
        var (service, gateway) = await PrepareAsync(publish: true, extra);
        return (new RecordEngine(service, gateway, new FixedClock(Now)), gateway);
    }

    private static async Task<(RecordEngine Engine, FakeRecordGateway Gateway)> DraftOnlyStationAsync()
    {
        var (service, gateway) = await PrepareAsync(publish: false, extra: null);
        return (new RecordEngine(service, gateway, new FixedClock(Now)), gateway);
    }

    private static async Task<(MetadataService Service, FakeRecordGateway Gateway)> PrepareAsync(
        bool publish, FieldDefinition[]? extra)
    {
        var store = new InMemoryMetadataStore();
        var inspector = new InMemorySchemaInspector();
        var service = new MetadataService(store, inspector, new SchemaChangePlanner());

        var definition = extra is null ? TestEntity.Station() : TestEntity.StationWith(extra);
        await service.CreateAsync(AppCode, definition, null, Ct);
        if (!publish) return (service, new FakeRecordGateway());

        var blocked = await service.PublishAsync(AppCode, EntityCode, null, Ct);
        inspector.CreateTable("LC_MES_STATION", "CODE", "NAME", "IS_ENABLED");
        blocked.PendingChanges[0].Status = SchemaChangeStatus.Applied;
        var published = await service.PublishAsync(AppCode, EntityCode, null, Ct);
        Assert.True(published.Published);

        return (service, new FakeRecordGateway());
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
