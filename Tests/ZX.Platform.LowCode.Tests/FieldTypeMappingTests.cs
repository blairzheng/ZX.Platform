using ZX.Platform.LowCode.Domain;
using ZX.Platform.LowCode.Infrastructure.Oracle;

namespace ZX.Platform.LowCode.Tests;

public class FieldTypeMappingTests
{
    [Theory]
    [InlineData(FieldType.Text, null, "NVARCHAR2(200)")]
    [InlineData(FieldType.Text, 64, "NVARCHAR2(64)")]
    [InlineData(FieldType.LongText, null, "CLOB")]
    [InlineData(FieldType.Number, null, "NUMBER(18,0)")]
    [InlineData(FieldType.Amount, null, "NUMBER(18,4)")]
    [InlineData(FieldType.Percent, null, "NUMBER(9,4)")]
    [InlineData(FieldType.Date, null, "DATE")]
    [InlineData(FieldType.DateTime, null, "TIMESTAMP(7) WITH TIME ZONE")]
    [InlineData(FieldType.Boolean, null, "NUMBER(1)")]
    [InlineData(FieldType.Select, null, "NVARCHAR2(64)")]
    [InlineData(FieldType.MultiSelect, null, "NVARCHAR2(2000)")]
    [InlineData(FieldType.Relation, null, "RAW(16)")]
    [InlineData(FieldType.Attachment, null, "CLOB")]
    [InlineData(FieldType.AutoNumber, null, "NVARCHAR2(40)")]
    public void 字段类型映射到Oracle列型(FieldType type, int? maxLength, string expected)
    {
        var field = new FieldDefinition { Key = "x", Type = type, Name = "x", MaxLength = maxLength };
        Assert.Equal(expected, OracleTypeMapper.ColumnType(field));
    }

    [Fact]
    public void number精度可配置()
    {
        var field = new FieldDefinition { Key = "qty", Type = FieldType.Number, Name = "数量", Precision = 12 };
        Assert.Equal("NUMBER(12,0)", OracleTypeMapper.ColumnType(field));
    }

    [Fact]
    public void 十三种类型全部有中文名和线名()
    {
        Assert.Equal(13, FieldTypes.All.Count);

        foreach (var type in FieldTypes.All)
        {
            var wireName = type.ToWireName();
            Assert.False(string.IsNullOrWhiteSpace(wireName));
            Assert.True(FieldTypes.TryParse(wireName, out var parsed));
            Assert.Equal(type, parsed);
        }
    }

    [Fact]
    public void 未知类型不能解析()
    {
        Assert.False(FieldTypes.TryParse("formula", out _));
        Assert.False(FieldTypes.TryParse(null, out _));
    }

    [Fact]
    public void 只有clob类型不可索引()
    {
        Assert.False(FieldTypeRules.Indexable(FieldType.LongText));
        Assert.False(FieldTypeRules.Indexable(FieldType.Attachment));
        Assert.True(FieldTypeRules.Indexable(FieldType.Text));
        Assert.True(FieldTypeRules.Indexable(FieldType.Relation));
    }

    [Fact]
    public void 长文本与多选不可排序()
    {
        Assert.False(FieldTypeRules.Sortable(FieldType.LongText));
        Assert.False(FieldTypeRules.Sortable(FieldType.MultiSelect));
        Assert.False(FieldTypeRules.Sortable(FieldType.Attachment));
        Assert.True(FieldTypeRules.Sortable(FieldType.Text));
    }

    [Theory]
    [InlineData(FieldType.Text, QueryOperator.Like, true)]
    [InlineData(FieldType.Text, QueryOperator.Gt, false)]
    [InlineData(FieldType.LongText, QueryOperator.IsNull, true)]
    [InlineData(FieldType.LongText, QueryOperator.Like, false)]
    [InlineData(FieldType.Number, QueryOperator.Between, true)]
    [InlineData(FieldType.Date, QueryOperator.Gt, false)]
    [InlineData(FieldType.Boolean, QueryOperator.Ne, false)]
    [InlineData(FieldType.MultiSelect, QueryOperator.Contains, true)]
    [InlineData(FieldType.MultiSelect, QueryOperator.Eq, false)]
    [InlineData(FieldType.Attachment, QueryOperator.IsNull, false)]
    public void 类型与算子的合法组合(FieldType type, QueryOperator op, bool expected)
    {
        Assert.Equal(expected, FieldTypeRules.Supports(type, op));
    }
}
