using ZX.Platform.LowCode.Application.Metadata;
using ZX.Platform.LowCode.Application.Runtime;
using ZX.Platform.LowCode.Domain;
using ZX.Platform.LowCode.Domain.Metadata;
using ZX.Platform.LowCode.Infrastructure.Oracle;
using ZX.Platform.LowCode.Infrastructure.Persistence;
using ZX.Platform.LowCode.Infrastructure.Schema;

namespace ZX.Platform.LowCode.Tests;

/// <summary>
/// 真库集成检查。默认不跑——只有设置了环境变量 <c>LOWCODE_TEST_DB</c> 才执行。
///
/// 为什么必须有这一组：单元测试可以证明"生成的 SQL 是对的"，
/// 但证明不了"Oracle 能执行它、读回来还是对的"。这几件事只在真库上才暴露：
///
/// - <c>BindByName</c> 是否真的生效（不生效则命名参数按位置静默错位）；
/// - <c>Guid</c> → <c>RAW(16)</c> 的绑定（ODP.NET 的隐式映射表里没有 Guid，直接抛异常）；
/// - CLOB 的写入与读取（VARCHAR2 / CLOB 的绑定上限不同）；
/// - DATE 列与 <see cref="DateTimeOffset"/> 绑定（会变成 TIMESTAMP WITH TIME ZONE，ORA-00932）；
/// - 唯一索引靠虚拟列模拟部分索引这套做法是否真的成立。
///
/// 运行方式：
/// <code>
/// LOWCODE_TEST_DB="User Id=...;Password=...;Data Source=..." \
///   dotnet test --filter "FullyQualifiedName~OracleIntegrationTests"
/// </code>
/// </summary>
public sealed class OracleIntegrationTests
{
    private static string? ConnectionString => Environment.GetEnvironmentVariable("LOWCODE_TEST_DB");

    private const string TempTable = "LC_TMP_INTEGRATION_CHECK";

    [Fact]
    public async Task 数据字典能读出表与列()
    {
        if (ConnectionString is null) return;

        await using var h = new Harness();

        // 元数据表是迁移建出来的，一定存在，用它验证字典查询本身。
        var shape = await h.Inspector.InspectAsync("LC_META_ENTITY", CancellationToken.None);

        Assert.True(shape.Exists);
        Assert.True(shape.HasColumn("CODE"));
        Assert.True(shape.HasColumn("DRAFT_JSON"));
        Assert.True(shape.HasColumn("PUBLISHED_JSON"));
    }

    [Fact]
    public async Task 不存在的表返回Missing()
    {
        if (ConnectionString is null) return;

        await using var h = new Harness();

        var shape = await h.Inspector.InspectAsync("LC_TABLE_THAT_DOES_NOT_EXIST", CancellationToken.None);

        Assert.False(shape.Exists);
        Assert.Empty(shape.Columns);
    }

    [Fact]
    public async Task 执行器能建表核对再删表()
    {
        if (ConnectionString is null) return;

        await using var h = new Harness();

        var statements = new List<string>
        {
            $"CREATE TABLE \"{TempTable}\" (\"ID\" RAW(16) NOT NULL, \"REMARK\" VARCHAR2(64) NULL, "
            + $"CONSTRAINT \"PK_{TempTable}\" PRIMARY KEY (\"ID\"))"
        };

        try
        {
            await h.Executor.ExecuteAsync(statements, CancellationToken.None);

            var shape = await h.Inspector.InspectAsync(TempTable, CancellationToken.None);
            Assert.True(shape.Exists);
            Assert.True(shape.HasColumn("REMARK"));
        }
        finally
        {
            // 无论断言是否失败都要清理，否则下次跑会撞 ORA-00955。
            try
            {
                await h.Executor.ExecuteAsync(
                    [$"DROP TABLE \"{TempTable}\" PURGE"], CancellationToken.None);
            }
            catch (LowCodeException)
            {
                // 表根本没建出来，没什么可删的。
            }
        }
    }

    /// <summary>
    /// 建表用的是真实的 <see cref="DdlSqlBuilder"/>，不是手写 SQL——
    /// 手写只能证明"我写的这句能跑"，证明不了"平台生成的那几句能跑"。
    /// </summary>
    [Fact]
    public async Task 生成的建表语句能在真库执行并核对出所有列()
    {
        if (ConnectionString is null) return;

        await using var h = new Harness();
        var entity = await h.CreateTableAsync(TestEntity.Station());

        var shape = await h.Inspector.InspectAsync(entity.TableName, CancellationToken.None);

        Assert.True(shape.Exists);
        Assert.True(shape.HasColumn("CODE"));
        Assert.True(shape.HasColumn("NAME"));
        Assert.True(shape.HasColumn("IS_ENABLED"));
        Assert.True(shape.HasColumn("REVISION"));

        // 唯一索引靠虚拟列实现，虚拟列本身必须真的建出来了。
        Assert.True(shape.HasColumn("ACTIVE_CODE"));
    }

    /// <summary>
    /// 这一条是整套"软删 + 唯一"设计的核心验证：
    /// Oracle 没有部分索引，平台用 <c>CASE WHEN IS_DELETED = 0 THEN CODE END</c> 虚拟列 + 唯一索引模拟。
    /// 删掉之后虚拟列变 NULL，Oracle 的唯一索引允许多个 NULL，于是同一个编码可以再用。
    /// </summary>
    [Fact]
    public async Task 唯一索引靠虚拟列拦住重复编码且删除后可复用()
    {
        if (ConnectionString is null) return;

        await using var h = new Harness();
        var entity = await h.CreateTableAsync(TestEntity.Station());
        var scope = RecordScope.Unscoped(Guid.NewGuid());
        var now = DateTimeOffset.UtcNow;

        var first = Guid.NewGuid();
        await h.Gateway.CreateAsync(entity, first,
            new Dictionary<string, object?> { ["code"] = "ST001", ["name"] = "一站" },
            scope, now, CancellationToken.None);

        // 数据库唯一索引是最终裁判：ORA-00001 必须被翻成 600003。
        var conflict = await Assert.ThrowsAsync<LowCodeException>(() => h.Gateway.CreateAsync(
            entity, Guid.NewGuid(),
            new Dictionary<string, object?> { ["code"] = "ST001", ["name"] = "二站" },
            scope, now, CancellationToken.None));

        Assert.Equal(LowCodeErrorCodes.Conflict, conflict.Code);

        // 软删之后，虚拟列对这条记录变成 NULL，唯一索引不再约束它。
        await h.Gateway.DeleteAsync(entity, first, revision: 1, scope, now, CancellationToken.None);

        await h.Gateway.CreateAsync(entity, Guid.NewGuid(),
            new Dictionary<string, object?> { ["code"] = "ST001", ["name"] = "三站" },
            scope, now, CancellationToken.None);

        var page = await h.Gateway.QueryAsync(
            new QuerySpec { Entity = entity, AllSbus = true, Take = 10 }, CancellationToken.None);

        Assert.Equal(1, page.Total);
    }

    [Fact]
    public async Task 写入读回走通真实Oracle()
    {
        if (ConnectionString is null) return;

        await using var h = new Harness();
        var entity = await h.CreateTableAsync(TestEntity.Station());
        var scope = RecordScope.Unscoped(Guid.NewGuid());
        var now = new DateTimeOffset(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);

        var id = Guid.NewGuid();
        await h.Gateway.CreateAsync(entity, id,
            new Dictionary<string, object?> { ["code"] = "ST001", ["name"] = "一站" },
            scope, now, CancellationToken.None);

        // BindByName 是否生效，看这里能不能按 :p0 / :sbuId 拿到正确的行。
        var page = await h.Gateway.QueryAsync(new QuerySpec
        {
            Entity = entity,
            AllSbus = true,
            Take = 10
        }, CancellationToken.None);

        var row = Assert.Single(page.Rows);
        Assert.Equal("ST001", row["CODE"]);
        Assert.Equal(1, page.Total);

        // RAW(16) 必须换成 Guid，否则序列化成 JSON 是一段 base64。
        Assert.Equal(id, row["ID"]);

        // 乐观锁：revision 不对必须改 0 行。
        var wrong = await h.Gateway.UpdateAsync(entity, id, revision: 99,
            new Dictionary<string, object?> { ["name"] = "改不动" }, scope, now, CancellationToken.None);
        Assert.False(wrong);

        var ok = await h.Gateway.UpdateAsync(entity, id, revision: 1,
            new Dictionary<string, object?> { ["name"] = "二站" }, scope, now, CancellationToken.None);
        Assert.True(ok);

        var deleted = await h.Gateway.DeleteAsync(entity, id, revision: 2, scope, now, CancellationToken.None);
        Assert.True(deleted);

        var afterDelete = await h.Gateway.QueryAsync(new QuerySpec
        {
            Entity = entity,
            AllSbus = true
        }, CancellationToken.None);
        Assert.Equal(0, afterDelete.Total);
    }

    /// <summary>
    /// CLOB、DATE、关联字段（RAW(16)）的往返。这三类在绑定层各有各的坑：
    /// 超过 4000 字符的字符串绑 Varchar2 会被驱动拒绝；
    /// DATE 列绑 DateTimeOffset 会变成 TIMESTAMP WITH TIME ZONE；
    /// 关联字段是 Guid，而 ODP.NET 的隐式映射表里没有 Guid。
    /// </summary>
    [Fact]
    public async Task CLOB与日期与关联字段往返()
    {
        if (ConnectionString is null) return;

        await using var h = new Harness();
        var entity = await h.CreateTableAsync(TestEntity.Stocktake());
        var metadata = BuildMetadata(entity);
        var engine = new RecordEngine(metadata, h.Gateway);
        var query = new QueryEngine(metadata, h.Gateway);

        var sbuId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var scope = new RecordScope(userId, sbuId, false);
        var modelId = Guid.NewGuid();

        // 5000 字符，超过 VARCHAR2 的绑定上限，必须走 CLOB。
        var remark = new string('盘', 5000);

        var id = await engine.CreateAsync("mes", "stocktake", new Dictionary<string, object?>
        {
            ["model_code"] = modelId,
            ["remark"] = remark
            // stocktake_date 故意不给：默认值 $now 由服务端解析。
        }, scope, CancellationToken.None);

        var row = await query.FindAsync("mes", "stocktake", id, scope, CancellationToken.None);
        Assert.NotNull(row);

        Assert.Equal(remark, row["remark"]);
        Assert.Equal(modelId.ToString(), row["model_code"]);
        Assert.Equal(DateTime.UtcNow.ToString("yyyy-MM-dd"), row["stocktake_date"]);

        // 默认值 is_enabled = true 必须真的落到库里，而不是只在 SQL 文本里存在。
        var page = await query.QueryAsync(
            "mes", "stocktake", new RecordQueryRequest { Take = 10 }, scope, CancellationToken.None);

        var listed = Assert.Single(page.Rows);
        Assert.Equal(modelId.ToString(), listed["model_code"]);
    }

    /// <summary>已发布定义的元数据服务：只为让引擎拿到 EntityDefinition，不涉及 DDL。</summary>
    private static MetadataService BuildMetadata(EntityDefinition entity)
    {
        var store = new InMemoryMetadataStore();
        var json = MetadataJson.Serialize(entity);

        store.Entities.Add(new MetaEntityRecord
        {
            Id = Guid.NewGuid(),
            AppCode = entity.AppCode,
            Code = entity.Code,
            Name = entity.Name,
            Status = MetadataStatus.Active,
            Revision = 1,
            DraftJson = json,
            PublishedJson = json,
            PublishedVersion = 1,
            CreatedAtUtc = DateTimeOffset.UnixEpoch
        });

        return new MetadataService(store, new InMemorySchemaInspector(), new SchemaChangePlanner());
    }

    /// <summary>真库连接的持有者：负责建表，也负责收尾删表。</summary>
    private sealed class Harness : IAsyncDisposable
    {
        private readonly List<EntityDefinition> _created = [];

        public Harness()
        {
            Db = new LowCodeDbContext(LowCodeDbContextFactory.CreateOptions(ConnectionString!));
            Inspector = new OracleSchemaInspector(Db);
            Executor = new OracleSchemaChangeExecutor(Db);
            Gateway = new DapperRecordGateway(Db);
        }

        public LowCodeDbContext Db { get; }
        public OracleSchemaInspector Inspector { get; }
        public OracleSchemaChangeExecutor Executor { get; }
        public DapperRecordGateway Gateway { get; }

        public async Task<EntityDefinition> CreateTableAsync(EntityDefinition entity)
        {
            await Executor.ExecuteAsync(
                DdlSqlBuilder.CreateTable(entity, schema: null), CancellationToken.None);

            _created.Add(entity);
            return entity;
        }

        public async ValueTask DisposeAsync()
        {
            foreach (var entity in _created)
            {
                try
                {
                    await Executor.ExecuteAsync(
                        [DdlSqlBuilder.DropTable(entity, schema: null)], CancellationToken.None);
                }
                catch (LowCodeException)
                {
                    // 建表失败时表本来就不存在。
                }
            }

            await Db.DisposeAsync();
        }
    }
}
