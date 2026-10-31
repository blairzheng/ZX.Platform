using ZX.Platform.LowCode.Domain;
using ZX.Platform.LowCode.Infrastructure.Oracle;

namespace ZX.Platform.LowCode.Tests;

public class WriteSqlBuilderTests
{
    private static readonly Guid UserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTimeOffset Now = new(2026, 10, 30, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void 新增自动补齐保留列与默认值()
    {
        var id = Guid.NewGuid();
        var query = WriteSqlBuilder.BuildInsert(new InsertRequest
        {
            Entity = TestEntity.Station(),
            Id = id,
            Context = new WriteContext(UserId, null, Now),
            Values = new Dictionary<string, object?>
            {
                ["code"] = "ST001",
                ["name"] = "一站"
            }
        });

        Assert.Equal(
            "INSERT INTO \"LC_MES_STATION\" "
            + "(\"ID\",\"CODE\",\"NAME\",\"IS_ENABLED\",\"CREATED_AT_UTC\",\"CREATED_BY_USER_ID\","
            + "\"UPDATED_AT_UTC\",\"UPDATED_BY_USER_ID\",\"IS_DELETED\",\"REVISION\") "
            + "VALUES (:p0,:p1,:p2,:p3,:p4,:p5,:p6,:p7,:p8,:p9)",
            query.Sql);

        Assert.Equal(id, query.Parameters[0].Value);
        Assert.Equal("ST001", query.Parameters[1].Value);
        Assert.Equal(1, query.Parameters[3].Value);   // is_enabled 默认 true
        Assert.Equal(Now, query.Parameters[4].Value);
        Assert.Equal(0, query.Parameters[8].Value);
        Assert.Equal(1, query.Parameters[9].Value);
    }

    [Fact]
    public void 缺少必填字段报600001()
    {
        var error = Assert.Throws<LowCodeException>(() => WriteSqlBuilder.BuildInsert(new InsertRequest
        {
            Entity = TestEntity.Station(),
            Id = Guid.NewGuid(),
            Context = new WriteContext(UserId, null, Now),
            Values = new Dictionary<string, object?> { ["name"] = "一站" }
        }));

        Assert.Equal(LowCodeErrorCodes.ValidationFailed, error.Code);
    }

    [Fact]
    public void 要求SBU的实体缺SBU时报610001()
    {
        var error = Assert.Throws<LowCodeException>(() => WriteSqlBuilder.BuildInsert(new InsertRequest
        {
            Entity = TestEntity.Stocktake(),
            Id = Guid.NewGuid(),
            Context = new WriteContext(UserId, null, Now),
            Values = new Dictionary<string, object?> { ["model_code"] = Guid.NewGuid() }
        }));

        Assert.Equal(LowCodeErrorCodes.SbuRequired, error.Code);
    }

    [Fact]
    public void 归属由服务端赋值客户端传了也无效()
    {
        var sbuId = Guid.NewGuid();
        var query = WriteSqlBuilder.BuildInsert(new InsertRequest
        {
            Entity = TestEntity.Stocktake(),
            Id = Guid.NewGuid(),
            Context = new WriteContext(UserId, sbuId, Now),
            Values = new Dictionary<string, object?>
            {
                ["model_code"] = Guid.NewGuid(),
                ["sbu_id"] = Guid.NewGuid(),   // 伪造的归属
                ["stocktake_date"] = "2026-10-01"
            }
        });

        Assert.Contains("\"SBU_ID\"", query.Sql);
        Assert.Contains(query.Parameters, p => Equals(p.Value, sbuId));
        Assert.DoesNotContain(query.Parameters, p => p.Name == "sbu_id");
    }

    [Fact]
    public void 修改带乐观锁并自增版本()
    {
        var id = Guid.NewGuid();
        var query = WriteSqlBuilder.BuildUpdate(new UpdateRequest
        {
            Entity = TestEntity.Station(),
            Id = id,
            Revision = 3,
            Context = new WriteContext(UserId, null, Now),
            Values = new Dictionary<string, object?> { ["name"] = "二站" }
        });

        Assert.Contains("SET \"NAME\" = :p0", query.Sql);
        Assert.Contains("\"REVISION\" = \"REVISION\" + 1", query.Sql);
        Assert.Contains("WHERE \"ID\" = :p3 AND \"REVISION\" = :p4 AND \"IS_DELETED\" = 0", query.Sql);
        Assert.Equal(3, query.Parameters.Single(p => p.Name == "p4").Value);
    }

    [Fact]
    public void 修改不允许变更归属()
    {
        var error = Assert.Throws<LowCodeException>(() => WriteSqlBuilder.BuildUpdate(new UpdateRequest
        {
            Entity = TestEntity.Stocktake(),
            Id = Guid.NewGuid(),
            Revision = 1,
            Context = new WriteContext(UserId, Guid.NewGuid(), Now),
            Values = new Dictionary<string, object?> { ["sbu_id"] = Guid.NewGuid() }
        }));

        Assert.Equal(LowCodeErrorCodes.Forbidden, error.Code);
    }

    [Fact]
    public void 空更新被拒绝()
    {
        Assert.Throws<LowCodeException>(() => WriteSqlBuilder.BuildUpdate(new UpdateRequest
        {
            Entity = TestEntity.Station(),
            Id = Guid.NewGuid(),
            Revision = 1,
            Context = new WriteContext(UserId, null, Now)
        }));
    }

    [Fact]
    public void 软删只改标记不动物理行()
    {
        var id = Guid.NewGuid();
        var query = WriteSqlBuilder.BuildSoftDelete(
            TestEntity.Station(), id, 5, new WriteContext(UserId, null, Now));

        Assert.Contains("\"IS_DELETED\" = 1", query.Sql);
        Assert.Contains("WHERE \"ID\" = :id AND \"REVISION\" = :rev AND \"IS_DELETED\" = 0", query.Sql);
        Assert.Equal(id, query.Parameters.Single(p => p.Name == "id").Value);
        Assert.Equal(5, query.Parameters.Single(p => p.Name == "rev").Value);
    }

    [Fact]
    public void 默认值now解析为写入时刻()
    {
        var query = WriteSqlBuilder.BuildInsert(new InsertRequest
        {
            Entity = TestEntity.Stocktake(),
            Id = Guid.NewGuid(),
            Context = new WriteContext(UserId, Guid.NewGuid(), Now),
            Values = new Dictionary<string, object?> { ["model_code"] = Guid.NewGuid() }
        });

        Assert.Contains(query.Parameters, p => Equals(p.Value, Now));
    }
}
