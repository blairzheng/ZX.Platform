using ZX.Platform.LowCode.Domain;
using ZX.Platform.LowCode.Infrastructure;
using ZX.Platform.LowCode.Infrastructure.Oracle;

namespace ZX.Platform.LowCode.Tests;

public class SelectSqlBuilderTests
{
    private static SqlQuery Build(QuerySpec spec) => SelectSqlBuilder.BuildSelect(spec);
    private static SqlQuery Count(QuerySpec spec) => SelectSqlBuilder.BuildCount(spec);

    private static QuerySpec Request(EntityDefinition entity, params FilterCondition[] filters) => new()
    {
        Entity = entity,
        Filters = filters
    };

    [Fact]
    public void 基础查询默认按ID排序并分页()
    {
        var query = Build(Request(TestEntity.Station()));

        Assert.Equal(
            "SELECT \"ID\",\"CODE\",\"NAME\",\"IS_ENABLED\" FROM \"LC_MES_STATION\" "
            + "WHERE \"IS_DELETED\" = 0 ORDER BY \"ID\" ASC "
            + "OFFSET :skip ROWS FETCH NEXT :take ROWS ONLY",
            query.Sql);

        Assert.Equal(0, query.Parameters.Single(p => p.Name == "skip").Value);
        Assert.Equal(20, query.Parameters.Single(p => p.Name == "take").Value);
    }

    [Fact]
    public void 计数查询不带排序和分页()
    {
        var query = Count(Request(TestEntity.Station()));

        Assert.Equal("SELECT COUNT(*) FROM \"LC_MES_STATION\" WHERE \"IS_DELETED\" = 0", query.Sql);
        Assert.Empty(query.Parameters);
    }

    [Fact]
    public void 要求SBU的实体缺SBU时报610001()
    {
        var error = Assert.Throws<LowCodeException>(() =>
            Build(Request(TestEntity.Station(sbuScope: true))));

        Assert.Equal(LowCodeErrorCodes.SbuRequired, error.Code);
    }

    [Fact]
    public void 带SBU时追加绑定变量()
    {
        var sbuId = Guid.NewGuid();
        var query = Build(new QuerySpec
        {
            Entity = TestEntity.Station(sbuScope: true),
            SbuId = sbuId
        });

        Assert.Contains("AND \"SBU_ID\" = :sbuId", query.Sql);
        Assert.Equal(sbuId, query.Parameters.Single(p => p.Name == "sbuId").Value);
    }

    [Fact]
    public void 拥有read_all时跳过SBU过滤()
    {
        var query = Build(new QuerySpec
        {
            Entity = TestEntity.Station(sbuScope: true),
            AllSbus = true
        });

        Assert.DoesNotContain("SBU_ID", query.Sql);
    }

    [Fact]
    public void 等值筛选参数化()
    {
        var query = Build(
            Request(TestEntity.Station(), new FilterCondition("code", QueryOperator.Eq, "ST001")));

        Assert.Contains("AND \"CODE\" = :p0", query.Sql);
        Assert.Equal("ST001", query.Parameters.Single(p => p.Name == "p0").Value);
    }

    [Fact]
    public void 前缀匹配追加百分号并转义通配符()
    {
        var query = Build(
            Request(TestEntity.Station(), new FilterCondition("code", QueryOperator.Like, "50%_A")));

        Assert.Contains("AND \"CODE\" LIKE :p0 ESCAPE '\\'", query.Sql);
        Assert.Equal("50\\%\\_A%", query.Parameters.Single(p => p.Name == "p0").Value);
    }

    [Fact]
    public void in条件展开成多个占位符()
    {
        var query = Build(
            Request(TestEntity.Station(), new FilterCondition("code", QueryOperator.In, Values: ["A", "B"])));

        Assert.Contains("AND \"CODE\" IN (:p0,:p1)", query.Sql);
    }

    [Fact]
    public void 空in条件直接不返回数据()
    {
        var query = Build(
            Request(TestEntity.Station(), new FilterCondition("code", QueryOperator.In, Values: [])));

        Assert.Contains("AND 1 = 0", query.Sql);
    }

    [Fact]
    public void between需要两个值()
    {
        QuerySpec StocktakeRequest(params FilterCondition[] filters) => new()
        {
            Entity = TestEntity.Stocktake(),
            SbuId = Guid.NewGuid(),
            Filters = filters
        };

        var query = Build(StocktakeRequest(
            new FilterCondition("stocktake_date", QueryOperator.Between, Values: ["2026-01-01", "2026-12-31"])));

        Assert.Contains("AND \"STOCKTAKE_DATE\" BETWEEN :p1 AND :p2", query.Sql);

        Assert.Throws<LowCodeException>(() => Build(StocktakeRequest(
            new FilterCondition("stocktake_date", QueryOperator.Between, Values: ["2026-01-01"]))));
    }

    [Fact]
    public void 多选字段的contains近似匹配()
    {
        var entity = new EntityDefinition
        {
            AppCode = "mes",
            Code = "tag",
            Name = "标签",
            Fields =
            [
                new FieldDefinition
                {
                    Key = "tags",
                    Type = FieldType.MultiSelect,
                    Name = "标签",
                    Options = [new FieldOption { Value = "urgent", Label = "紧急" }]
                }
            ]
        };

        var query = Build(
            Request(entity, new FilterCondition("tags", QueryOperator.Contains, "urgent")));

        Assert.Contains("AND \"TAGS\" LIKE :p0 ESCAPE '\\'", query.Sql);
        Assert.Equal("%\"urgent\"%", query.Parameters.Single(p => p.Name == "p0").Value);
    }

    [Fact]
    public void 布尔值转成0和1()
    {
        var query = Build(
            Request(TestEntity.Station(), new FilterCondition("is_enabled", QueryOperator.Eq, "true")));

        Assert.Equal(1, query.Parameters.Single(p => p.Name == "p0").Value);
    }

    [Fact]
    public void 类型不支持的算子报610003()
    {
        var error = Assert.Throws<LowCodeException>(() => Build(
            Request(TestEntity.Station(), new FilterCondition("code", QueryOperator.Gt, "A"))));

        Assert.Equal(LowCodeErrorCodes.OperatorNotAllowed, error.Code);
    }

    [Fact]
    public void 未知字段报610003()
    {
        var error = Assert.Throws<LowCodeException>(() => Build(
            Request(TestEntity.Station(), new FilterCondition("nope", QueryOperator.Eq, "x"))));

        Assert.Equal(LowCodeErrorCodes.OperatorNotAllowed, error.Code);
    }

    [Fact]
    public void 分页大小被截断到上限()
    {
        var query = Build(new QuerySpec
        {
            Entity = TestEntity.Station(),
            Take = 1000
        });

        Assert.Equal(SelectSqlBuilder.MaxPageSize, query.Parameters.Single(p => p.Name == "take").Value);
    }

    [Fact]
    public void 深翻页报610004()
    {
        var error = Assert.Throws<LowCodeException>(() => Build(new QuerySpec
        {
            Entity = TestEntity.Station(),
            Skip = 20_000
        }));

        Assert.Equal(LowCodeErrorCodes.ResultTooLarge, error.Code);
    }

    [Fact]
    public void 投影只能包含已定义字段()
    {
        var query = Build(new QuerySpec
        {
            Entity = TestEntity.Station(),
            Fields = ["code"]
        });

        Assert.StartsWith("SELECT \"ID\",\"CODE\" FROM", query.Sql);

        Assert.Throws<LowCodeException>(() => Build(new QuerySpec
        {
            Entity = TestEntity.Station(),
            Fields = ["nope"]
        }));
    }

    [Fact]
    public void 不可排序字段不能作为排序条件()
    {
        var error = Assert.Throws<LowCodeException>(() => Build(new QuerySpec
        {
            Entity = TestEntity.Stocktake(),
            SbuId = Guid.NewGuid(),
            Sorts = [new ViewSort { Field = "remark" }]
        }));

        Assert.Equal(LowCodeErrorCodes.OperatorNotAllowed, error.Code);
    }

    [Fact]
    public void 排序字段渲染成带引号的列名()
    {
        var query = Build(new QuerySpec
        {
            Entity = TestEntity.Station(),
            Sorts = [new ViewSort { Field = "code", Descending = true }]
        });

        Assert.Contains("ORDER BY \"CODE\" DESC", query.Sql);
    }
}
