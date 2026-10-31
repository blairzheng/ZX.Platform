using ZX.Platform.LowCode.Application.Runtime;
using ZX.Platform.LowCode.Domain;

namespace ZX.Platform.LowCode.Tests;

public class RecordValueMapperTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(true)]
    [InlineData("1")]
    public void 布尔字段各种来源都转成true(object raw)
    {
        Assert.Equal(true, RecordValueMapper.Convert(Field(FieldType.Boolean), raw));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(false)]
    [InlineData("false")]
    public void 布尔字段各种来源都转成false(object raw)
    {
        Assert.Equal(false, RecordValueMapper.Convert(Field(FieldType.Boolean), raw));
    }

    [Fact]
    public void 日期只保留年月日()
    {
        var raw = new DateTime(2026, 10, 30, 15, 42, 11, DateTimeKind.Utc);

        Assert.Equal("2026-10-30", RecordValueMapper.Convert(Field(FieldType.Date), raw));
    }

    [Fact]
    public void 时间按UTC输出并补Z后缀()
    {
        // 库里是 TIMESTAMP，没有时区。写入一律 UTC，读出补 Z，否则前端会再减一次本地时区。
        var raw = new DateTime(2026, 10, 30, 8, 0, 0, DateTimeKind.Unspecified);

        Assert.Equal("2026-10-30T08:00:00Z", RecordValueMapper.Convert(Field(FieldType.DateTime), raw));
    }

    [Fact]
    public void 金额保持decimal不转字符串()
    {
        Assert.Equal(12.5m, RecordValueMapper.Convert(Field(FieldType.Amount), 12.5m));
    }

    [Fact]
    public void 数字字段接受字符串输入()
    {
        Assert.Equal(42m, RecordValueMapper.Convert(Field(FieldType.Number), "42"));
    }

    [Fact]
    public void 多选解析成数组()
    {
        var value = RecordValueMapper.Convert(Field(FieldType.MultiSelect), "[\"urgent\",\"rework\"]");

        Assert.Equal(new[] { "urgent", "rework" }, Assert.IsAssignableFrom<IReadOnlyList<string>>(value));
    }

    [Fact]
    public void 多选脏数据返回空数组而不是抛错()
    {
        // 一条脏数据不该让整页查询失败。
        var value = RecordValueMapper.Convert(Field(FieldType.MultiSelect), "not-json");

        Assert.Empty(Assert.IsAssignableFrom<IReadOnlyList<string>>(value));
    }

    [Fact]
    public void 空值原样返回null()
    {
        Assert.Null(RecordValueMapper.Convert(Field(FieldType.Text), null));
        Assert.Null(RecordValueMapper.Convert(Field(FieldType.Text), DBNull.Value));
    }

    [Fact]
    public void 行映射把列名换成字段key并保留保留列()
    {
        var entity = TestEntity.Station();
        var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["ID"] = Guid.NewGuid(),
            ["REVISION"] = 3m,
            ["CODE"] = "ST001",
            ["NAME"] = "一站",
            ["IS_ENABLED"] = 1m
        };

        var wire = RecordValueMapper.ToWire(entity, row);

        Assert.Equal("ST001", wire["code"]);
        Assert.Equal(true, wire["is_enabled"]);
        Assert.Equal(3m, wire["revision"]);   // 保留列原样带出，键小写
        Assert.True(wire.ContainsKey("id"));
        // 输出键是字段 key（小写蛇形），不再出现大写列名。
        Assert.DoesNotContain("CODE", wire.Keys);
        Assert.Contains("code", wire.Keys);
    }

    private static FieldDefinition Field(FieldType type) => new()
    {
        Key = "value",
        Type = type,
        Name = "值"
    };
}
