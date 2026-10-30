using ZX.Platform.LowCode.Domain;
using ZX.Platform.LowCode.Infrastructure.Oracle;

namespace ZX.Platform.LowCode.Tests;

public class DdlSqlBuilderTests
{
    private const string Schema = "ZX_LOWCODE";

    [Fact]
    public void 建表包含主键与审计列()
    {
        var statements = DdlSqlBuilder.CreateTable(TestEntity.Station(), Schema);

        var expected = string.Join("\n",
            "CREATE TABLE \"ZX_LOWCODE\".\"LC_MES_STATION\" (",
            "  \"ID\" RAW(16) NOT NULL,",
            "  \"CODE\" NVARCHAR2(64) NOT NULL,",
            "  \"NAME\" NVARCHAR2(200) NOT NULL,",
            "  \"IS_ENABLED\" NUMBER(1) DEFAULT 1 NOT NULL,",
            "  \"CREATED_AT_UTC\" TIMESTAMP(7) WITH TIME ZONE NOT NULL,",
            "  \"CREATED_BY_USER_ID\" RAW(16),",
            "  \"UPDATED_AT_UTC\" TIMESTAMP(7) WITH TIME ZONE,",
            "  \"UPDATED_BY_USER_ID\" RAW(16),",
            "  \"IS_DELETED\" NUMBER(1) DEFAULT 0 NOT NULL,",
            "  \"REVISION\" NUMBER(10) DEFAULT 1 NOT NULL,",
            "  CONSTRAINT \"PK_LC_MES_STATION\" PRIMARY KEY (\"ID\")",
            ")");

        Assert.Equal(expected, statements[0]);
    }

    [Fact]
    public void 唯一字段用虚拟列加唯一索引模拟部分索引()
    {
        var statements = DdlSqlBuilder.CreateTable(TestEntity.Station(), Schema);

        Assert.Equal(3, statements.Count);
        Assert.Contains("GENERATED ALWAYS AS (CASE WHEN \"IS_DELETED\" = 0 THEN \"CODE\" END) VIRTUAL", statements[1]);
        Assert.StartsWith("CREATE UNIQUE INDEX \"UX_LC_MES_STATION_ACTIVE_CODE\"", statements[2]);
        Assert.EndsWith("ONLINE", statements[2]);
    }

    [Fact]
    public void 按SBU隔离时自动建索引()
    {
        var statements = DdlSqlBuilder.CreateTable(TestEntity.Station(sbuScope: true), Schema);

        Assert.Contains("\"SBU_ID\" RAW(16),", statements[0]);
        Assert.Contains(statements, sql => sql.Contains("\"IX_LC_MES_STATION_SBU_ID\""));
    }

    [Fact]
    public void 关联字段自动建索引()
    {
        var statements = DdlSqlBuilder.CreateTable(TestEntity.Stocktake(), Schema);

        Assert.Contains(statements, sql => sql.Contains("\"IX_LC_MES_STOCKTAKE_MODEL_CODE\""));
    }

    [Fact]
    public void 加列必须可空或带默认值()
    {
        var entity = TestEntity.Station();
        var required = new FieldDefinition { Key = "owner", Type = FieldType.Text, Name = "负责人", Required = true };

        var error = Assert.Throws<InvalidOperationException>(() =>
            DdlSqlBuilder.AddColumn(entity, Schema, required));

        Assert.Contains("可空", error.Message);
    }

    [Fact]
    public void 带默认值的必填列可以加()
    {
        var entity = TestEntity.Station();
        var field = new FieldDefinition
        {
            Key = "status",
            Type = FieldType.Text,
            Name = "状态",
            Required = true,
            DefaultValue = "draft"
        };

        var statements = DdlSqlBuilder.AddColumn(entity, Schema, field);

        Assert.Single(statements);
        Assert.Equal(
            "ALTER TABLE \"ZX_LOWCODE\".\"LC_MES_STATION\" ADD (\"STATUS\" NVARCHAR2(200) DEFAULT 'draft' NOT NULL)",
            statements[0]);
    }

    [Fact]
    public void 删表带PURGE()
    {
        Assert.Equal("DROP TABLE \"ZX_LOWCODE\".\"LC_MES_STATION\" PURGE",
            DdlSqlBuilder.DropTable(TestEntity.Station(), Schema));
    }

    [Fact]
    public void 索引名超长时被截断且仍在30字符内()
    {
        // LC_MES_MATERIAL_STOCKTAKE_LINE 正好 30 字符，表名合法；
        // 但 IX_ + 表名 + 列名 会超长，索引名必须截断。
        var entity = new EntityDefinition
        {
            AppCode = "mes",
            Code = "material_stocktake_line",
            Name = "盘点明细",
            Fields =
            [
                new FieldDefinition
                {
                    Key = "material_code",
                    Type = FieldType.Relation,
                    Name = "料号",
                    RefEntity = "material"
                }
            ]
        };

        Assert.Equal("LC_MES_MATERIAL_STOCKTAKE_LINE", entity.TableName);

        var statements = DdlSqlBuilder.CreateTable(entity, Schema);

        foreach (var identifier in statements.SelectMany(ExtractIdentifiers))
            Assert.True(identifier.Length <= OracleIdentifier.MaxLength, $"标识符 {identifier} 超长。");
    }

    [Fact]
    public void 表名超长直接拒绝而不是静默截断()
    {
        var entity = new EntityDefinition
        {
            AppCode = "production",
            Code = "material_stocktake_line",
            Name = "盘点明细",
            Fields = [new FieldDefinition { Key = "code", Type = FieldType.Text, Name = "编码" }]
        };

        Assert.Throws<InvalidOperationException>(() => DdlSqlBuilder.CreateTable(entity, Schema));
    }

    private static IEnumerable<string> ExtractIdentifiers(string statement)
    {
        var index = 0;
        while (index < statement.Length)
        {
            var start = statement.IndexOf('"', index);
            if (start < 0) yield break;

            var end = statement.IndexOf('"', start + 1);
            if (end < 0) yield break;

            yield return statement[(start + 1)..end];
            index = end + 1;
        }
    }

    [Fact]
    public void CLOB字段不允许唯一()
    {
        var entity = new EntityDefinition
        {
            AppCode = "mes",
            Code = "note",
            Name = "备注",
            Fields = [new FieldDefinition { Key = "body", Type = FieldType.LongText, Name = "正文", Unique = true }]
        };

        Assert.Throws<InvalidOperationException>(() => DdlSqlBuilder.CreateTable(entity, Schema));
    }
}
