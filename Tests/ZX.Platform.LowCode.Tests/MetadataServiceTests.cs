using ZX.Platform.LowCode.Application.Metadata;
using ZX.Platform.LowCode.Domain;
using ZX.Platform.LowCode.Domain.Metadata;
using ZX.Platform.LowCode.Infrastructure.Schema;

namespace ZX.Platform.LowCode.Tests;

/// <summary>
/// MetadataService 的四条硬规则。这里用内存仓储 + 内存"数据库现状"，
/// 因为规则本身与数据库无关——真正连库的部分由 SchemaChange 的执行服务负责。
/// </summary>
public sealed class MetadataServiceTests
{
    private const string AppCode = "mes";
    private const string EntityCode = "station";
    private const string TableName = "LC_MES_STATION";

    private static readonly CancellationToken Ct = CancellationToken.None;

    // ── 创建 ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_starts_as_draft_with_revision_one()
    {
        var h = new Harness();

        var record = await h.Service.CreateAsync(AppCode, TestEntity.Station(), null, Ct);

        Assert.Equal(MetadataStatus.Draft, record.Status);
        Assert.Equal(1, record.Revision);
        Assert.Equal(0, record.PublishedVersion);
        Assert.Null(record.PublishedJson);
        Assert.False(string.IsNullOrWhiteSpace(record.DraftJson));
    }

    [Fact]
    public async Task Create_rejects_app_code_mismatch()
    {
        var h = new Harness();

        var ex = await ThrowsAsync(LowCodeErrorCodes.ValidationFailed,
            () => h.Service.CreateAsync("wms", TestEntity.Station(), null, Ct));

        Assert.Contains("应用", ex.Message);
        Assert.Empty(h.Store.Entities);
    }

    [Fact]
    public async Task Create_rejects_duplicate_entity_code()
    {
        var h = new Harness();
        await h.Service.CreateAsync(AppCode, TestEntity.Station(), null, Ct);

        await ThrowsAsync(LowCodeErrorCodes.Conflict,
            () => h.Service.CreateAsync(AppCode, TestEntity.Station(), null, Ct));

        Assert.Single(h.Store.Entities);
    }

    [Fact]
    public async Task Create_rejects_invalid_definition()
    {
        var h = new Harness();
        var broken = new EntityDefinition
        {
            AppCode = AppCode,
            Code = EntityCode,
            Name = "站点",
            Fields = [new FieldDefinition { Key = "BadKey", Type = FieldType.Text, Name = "大写键" }]
        };

        await ThrowsAsync(LowCodeErrorCodes.ValidationFailed,
            () => h.Service.CreateAsync(AppCode, broken, null, Ct));

        Assert.Empty(h.Store.Entities);
    }

    // ── 读取 ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Get_draft_throws_when_entity_missing()
    {
        var h = new Harness();
        await ThrowsAsync(LowCodeErrorCodes.NotFound, () => h.Service.GetDraftAsync(AppCode, EntityCode, Ct));
    }

    [Fact]
    public async Task Get_published_returns_null_before_first_publish()
    {
        var h = new Harness();
        await h.Service.CreateAsync(AppCode, TestEntity.Station(), null, Ct);

        Assert.Null(await h.Service.GetPublishedAsync(AppCode, EntityCode, Ct));
    }

    // ── 保存草稿 ───────────────────────────────────────────────────────

    [Fact]
    public async Task Save_draft_bumps_revision()
    {
        var h = new Harness();
        await h.Service.CreateAsync(AppCode, TestEntity.Station(), null, Ct);

        var renamed = WithName(TestEntity.Station(), "站点（改）");

        await h.Service.SaveDraftAsync(AppCode, EntityCode, renamed, 1, null, Ct);

        var record = await h.Store.FindEntityAsync(AppCode, EntityCode, Ct);
        Assert.Equal(2, record!.Revision);
        Assert.Equal("站点（改）", record.Name);
    }

    [Fact]
    public async Task Save_draft_rejects_stale_revision()
    {
        var h = new Harness();
        await h.Service.CreateAsync(AppCode, TestEntity.Station(), null, Ct);
        await h.Service.SaveDraftAsync(AppCode, EntityCode, TestEntity.Station(), 1, null, Ct);

        // 第二次提交还用 revision = 1：说明有人拿着旧页面在编辑。
        var ex = await ThrowsAsync(LowCodeErrorCodes.Conflict,
            () => h.Service.SaveDraftAsync(AppCode, EntityCode, TestEntity.Station(), 1, null, Ct));

        Assert.Contains("revision", ex.Message);
    }

    [Fact]
    public async Task Save_draft_rejects_entity_code_change()
    {
        var h = new Harness();
        await h.Service.CreateAsync(AppCode, TestEntity.Station(), null, Ct);

        var renamed = new EntityDefinition
        {
            AppCode = AppCode,
            Code = "station_v2",
            Name = "站点",
            Fields = TestEntity.Station().Fields
        };

        await ThrowsAsync(LowCodeErrorCodes.ValidationFailed,
            () => h.Service.SaveDraftAsync(AppCode, EntityCode, renamed, 1, null, Ct));
    }

    [Fact]
    public async Task Save_draft_leaves_stored_json_untouched_when_invalid()
    {
        var h = new Harness();
        var record = await h.Service.CreateAsync(AppCode, TestEntity.Station(), null, Ct);
        var before = record.DraftJson;

        var broken = new EntityDefinition { AppCode = AppCode, Code = EntityCode, Name = "站点", Fields = [] };
        await ThrowsAsync(LowCodeErrorCodes.ValidationFailed,
            () => h.Service.SaveDraftAsync(AppCode, EntityCode, broken, 1, null, Ct));

        Assert.Equal(before, record.DraftJson);
        Assert.Equal(1, record.Revision);
    }

    // ── 发布：第一次 ───────────────────────────────────────────────────

    [Fact]
    public async Task Publish_without_draft_throws_state_invalid()
    {
        var h = new Harness();
        var record = await h.Service.CreateAsync(AppCode, TestEntity.Station(), null, Ct);
        record.DraftJson = null;

        await ThrowsAsync(LowCodeErrorCodes.StateInvalid,
            () => h.Service.PublishAsync(AppCode, EntityCode, null, Ct));
    }

    [Fact]
    public async Task Publish_first_time_creates_table_and_does_not_activate()
    {
        var h = new Harness();
        await h.Service.CreateAsync(AppCode, TestEntity.Station(), null, Ct);

        var result = await h.Service.PublishAsync(AppCode, EntityCode, null, Ct);

        Assert.False(result.Published);
        var change = Assert.Single(result.PendingChanges);
        Assert.Equal(SchemaChangeKind.CreateTable, change.Kind);
        Assert.Equal(SchemaChangeStatus.Pending, change.Status);
        Assert.Contains($"CREATE TABLE \"{TableName}\"", change.ScriptSql);
        Assert.Contains("\"CODE\"", change.ScriptSql);

        // 元数据没有激活：运行时读不到，就不会对着不存在的表发查询。
        Assert.Null(await h.Service.GetPublishedAsync(AppCode, EntityCode, Ct));
    }

    [Fact]
    public async Task Publish_does_not_regenerate_while_changes_are_pending()
    {
        var h = new Harness();
        await h.Service.CreateAsync(AppCode, TestEntity.Station(), null, Ct);
        var first = await h.Service.PublishAsync(AppCode, EntityCode, null, Ct);

        var savesBefore = h.Store.SaveCount;
        var second = await h.Service.PublishAsync(AppCode, EntityCode, null, Ct);

        Assert.False(second.Published);
        Assert.Equal(first.PendingChanges[0].Id, second.PendingChanges[0].Id);
        Assert.Single(h.Store.Changes);
        Assert.Equal(savesBefore, h.Store.SaveCount);
    }

    [Fact]
    public async Task Publish_activates_when_dba_already_created_the_table()
    {
        // 生产环境的常见情形：DBA 提前手工建好了表。此时不该再生成一次 CREATE TABLE。
        var h = new Harness();
        await h.Service.CreateAsync(AppCode, TestEntity.Station(), null, Ct);
        h.CreateStationTableInDb();

        var result = await h.Service.PublishAsync(AppCode, EntityCode, null, Ct);

        Assert.True(result.Published);
        Assert.Equal(1, result.Version);
        Assert.Empty(h.Store.Changes);
    }

    [Fact]
    public async Task Publish_after_create_table_applied_activates()
    {
        var h = new Harness();
        await h.Service.CreateAsync(AppCode, TestEntity.Station(), null, Ct);
        var blocked = await h.Service.PublishAsync(AppCode, EntityCode, null, Ct);

        h.CreateStationTableInDb();
        h.MarkApplied(blocked.PendingChanges[0]);

        var result = await h.Service.PublishAsync(AppCode, EntityCode, null, Ct);

        Assert.True(result.Published);
        Assert.Equal(1, result.Version);

        var record = await h.Store.FindEntityAsync(AppCode, EntityCode, Ct);
        Assert.Equal(MetadataStatus.Active, record!.Status);
        Assert.Equal(1, record.PublishedVersion);
        Assert.Equal(record.DraftJson, record.PublishedJson);
        Assert.Single(h.Store.Versions);
    }

    // ── 发布：加列 ─────────────────────────────────────────────────────

    [Fact]
    public async Task Publish_blocks_on_missing_column()
    {
        var h = new Harness();
        await PublishStationV1Async(h);

        await h.Service.SaveDraftAsync(AppCode, EntityCode, TestEntity.StationWith(SortNo()), 1, null, Ct);

        var result = await h.Service.PublishAsync(AppCode, EntityCode, null, Ct);

        Assert.False(result.Published);
        var change = Assert.Single(result.PendingChanges);
        Assert.Equal(SchemaChangeKind.AddColumn, change.Kind);
        Assert.Contains("ALTER TABLE", change.ScriptSql);
        Assert.Contains("\"SORT_NO\"", change.ScriptSql);
    }

    [Fact]
    public async Task Publish_does_not_loop_after_column_is_added()
    {
        // 这是最容易写错的一处：如果拿"已发布定义"当基线，加列执行完还会被重新算出来，
        // 实体就永远发布不了。以库为准之后这条回路必须一次走通。
        var h = new Harness();
        await PublishStationV1Async(h);

        await h.Service.SaveDraftAsync(AppCode, EntityCode, TestEntity.StationWith(SortNo()), 1, null, Ct);
        var blocked = await h.Service.PublishAsync(AppCode, EntityCode, null, Ct);

        h.Inspector.AddColumn(TableName, "SORT_NO");
        h.MarkApplied(blocked.PendingChanges[0]);

        var result = await h.Service.PublishAsync(AppCode, EntityCode, null, Ct);

        Assert.True(result.Published);
        Assert.Equal(2, result.Version);
        Assert.Equal(2, h.Store.Versions.Count);
    }

    [Fact]
    public async Task Publish_rejects_required_column_without_default()
    {
        var h = new Harness();
        await PublishStationV1Async(h);

        var required = new FieldDefinition
        {
            Key = "owner",
            Type = FieldType.Text,
            Name = "责任人",
            Required = true,
            Sort = 40
        };
        await h.Service.SaveDraftAsync(AppCode, EntityCode, TestEntity.StationWith(required), 1, null, Ct);

        // 表里已经有行，NOT NULL 且无默认值的列加不上去——必须在生成 DDL 之前就拒绝。
        var ex = await ThrowsAsync(LowCodeErrorCodes.ValidationFailed,
            () => h.Service.PublishAsync(AppCode, EntityCode, null, Ct));

        Assert.Contains("NOT NULL", ex.Message);
        Assert.DoesNotContain(h.Store.Changes, change => change.Status == SchemaChangeStatus.Pending);
    }

    // ── 发布：不允许的变更 ─────────────────────────────────────────────

    [Fact]
    public async Task Publish_rejects_field_type_change()
    {
        var h = new Harness();
        await PublishStationV1Async(h);

        var retyped = TestEntity.StationWithNameType(FieldType.LongText);
        await h.Service.SaveDraftAsync(AppCode, EntityCode, retyped, 1, null, Ct);

        var ex = await ThrowsAsync(LowCodeErrorCodes.ValidationFailed,
            () => h.Service.PublishAsync(AppCode, EntityCode, null, Ct));

        Assert.Contains("类型不能修改", ex.Message);
    }

    [Fact]
    public async Task Publish_rejects_table_name_change()
    {
        var h = new Harness();
        await PublishStationV1Async(h);

        // 改 appCode 会让物理表名从 LC_MES_STATION 变成 LC_WMS_STATION。
        var moved = new EntityDefinition
        {
            AppCode = "wms",
            Code = EntityCode,
            Name = "站点",
            Fields = TestEntity.Station().Fields
        };
        await h.Service.SaveDraftAsync(AppCode, EntityCode, moved, 1, null, Ct);

        var ex = await ThrowsAsync(LowCodeErrorCodes.ValidationFailed,
            () => h.Service.PublishAsync(AppCode, EntityCode, null, Ct));

        Assert.Contains("物理表名不能变更", ex.Message);
    }

    // ── 版本 ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Rollback_writes_version_back_to_draft()
    {
        var h = new Harness();
        await PublishStationV1Async(h);

        var renamed = WithName(TestEntity.Station(), "站点（第二版）");
        await h.Service.SaveDraftAsync(AppCode, EntityCode, renamed, 1, null, Ct);
        await h.Service.PublishAsync(AppCode, EntityCode, null, Ct);

        var restored = await h.Service.RollbackAsync(AppCode, EntityCode, 1, null, Ct);

        Assert.Equal("站点", restored.Name);
        var record = await h.Store.FindEntityAsync(AppCode, EntityCode, Ct);
        Assert.Equal("站点", record!.Name);
        Assert.Equal(3, record.Revision);

        // 回滚只改草稿，线上仍是第二版——回滚可能带结构变化，必须再走一次发布。
        Assert.Equal(2, record.PublishedVersion);
    }

    [Fact]
    public async Task Rollback_rejects_unknown_version()
    {
        var h = new Harness();
        await PublishStationV1Async(h);

        await ThrowsAsync(LowCodeErrorCodes.NotFound,
            () => h.Service.RollbackAsync(AppCode, EntityCode, 9, null, Ct));
    }

    // ── 辅助 ───────────────────────────────────────────────────────────

    /// <summary>把站点推到"已发布 v1、表已存在"的状态，作为加列类用例的起点。</summary>
    private static async Task PublishStationV1Async(Harness h)
    {
        await h.Service.CreateAsync(AppCode, TestEntity.Station(), null, Ct);
        var blocked = await h.Service.PublishAsync(AppCode, EntityCode, null, Ct);
        h.CreateStationTableInDb();
        h.MarkApplied(blocked.PendingChanges[0]);
        var published = await h.Service.PublishAsync(AppCode, EntityCode, null, Ct);
        Assert.True(published.Published);
    }

    private static FieldDefinition SortNo() => new()
    {
        Key = "sort_no",
        Type = FieldType.Number,
        Name = "排序号",
        Sort = 40
    };

    private static EntityDefinition WithName(EntityDefinition source, string name) => new()
    {
        AppCode = source.AppCode,
        Code = source.Code,
        Name = name,
        ScopeMode = source.ScopeMode,
        Fields = source.Fields
    };

    private static async Task<LowCodeException> ThrowsAsync(string expectedCode, Func<Task> action)
    {
        var ex = await Assert.ThrowsAsync<LowCodeException>(action);
        Assert.Equal(expectedCode, ex.Code);
        return ex;
    }

    private sealed class Harness
    {
        public InMemoryMetadataStore Store { get; } = new();
        public InMemorySchemaInspector Inspector { get; } = new();
        public MetadataService Service { get; }

        public Harness() => Service = new MetadataService(Store, Inspector, new SchemaChangePlanner());

        /// <summary>模拟 DDL 执行成功：库里有了表，变更记录置为 applied。</summary>
        public void CreateStationTableInDb() =>
            Inspector.CreateTable(TableName, "CODE", "NAME", "IS_ENABLED");

        public void MarkApplied(SchemaChangeRecord change)
        {
            change.Status = SchemaChangeStatus.Applied;
            change.ExecutedAtUtc = DateTimeOffset.UtcNow;
            change.VerifiedAtUtc = DateTimeOffset.UtcNow;
        }
    }
}
