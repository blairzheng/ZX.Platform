using ZX.Platform.LowCode.Domain;

namespace ZX.Platform.LowCode.Tests;

public class EntityDefinitionValidatorTests
{
    private static EntityDefinition WithField(FieldDefinition field) => new()
    {
        AppCode = "mes",
        Code = "sample",
        Name = "样例",
        Fields = [field]
    };

    [Fact]
    public void 合法实体没有错误()
    {
        Assert.Empty(EntityDefinitionValidator.Validate(TestEntity.Station()));
        Assert.Empty(EntityDefinitionValidator.Validate(TestEntity.Stocktake()));
    }

    [Fact]
    public void schema版本必须匹配()
    {
        var entity = new EntityDefinition
        {
            SchemaVersion = 2,
            AppCode = "mes",
            Code = "sample",
            Name = "样例",
            Fields = [new FieldDefinition { Key = "code", Type = FieldType.Text, Name = "编码" }]
        };

        Assert.Contains(EntityDefinitionValidator.Validate(entity),
            error => error.Rule == ValidationRules.SchemaVersionUnsupported);
    }

    [Theory]
    [InlineData("Sample")]
    [InlineData("1sample")]
    [InlineData("sample-dash")]
    [InlineData("")]
    public void 实体编码格式受限(string code)
    {
        var entity = new EntityDefinition
        {
            AppCode = "mes",
            Code = code,
            Name = "样例",
            Fields = [new FieldDefinition { Key = "code", Type = FieldType.Text, Name = "编码" }]
        };

        Assert.Contains(EntityDefinitionValidator.Validate(entity),
            error => error.Rule == ValidationRules.CodeInvalid);
    }

    [Fact]
    public void 物理表名不能超过30字符()
    {
        var entity = new EntityDefinition
        {
            AppCode = "production",
            Code = "material_stocktake_line",
            Name = "盘点明细",
            Fields = [new FieldDefinition { Key = "code", Type = FieldType.Text, Name = "编码" }]
        };

        Assert.Contains(EntityDefinitionValidator.Validate(entity),
            error => error.Rule == ValidationRules.TableNameTooLong);
    }

    [Fact]
    public void 字段key不能重复()
    {
        var entity = new EntityDefinition
        {
            AppCode = "mes",
            Code = "sample",
            Name = "样例",
            Fields =
            [
                new FieldDefinition { Key = "code", Type = FieldType.Text, Name = "编码" },
                new FieldDefinition { Key = "code", Type = FieldType.Text, Name = "编码2" }
            ]
        };

        Assert.Contains(EntityDefinitionValidator.Validate(entity),
            error => error.Rule == ValidationRules.FieldKeyDuplicate);
    }

    [Theory]
    [InlineData("id")]
    [InlineData("sbu_id")]
    [InlineData("is_deleted")]
    [InlineData("revision")]
    [InlineData("created_at_utc")]
    public void 字段key不能占用保留列(string key)
    {
        Assert.Contains(EntityDefinitionValidator.Validate(WithField(
                new FieldDefinition { Key = key, Type = FieldType.Text, Name = "冲突" })),
            error => error.Rule == ValidationRules.FieldKeyReserved);
    }

    [Fact]
    public void 文本长度超上限被拒()
    {
        Assert.Contains(EntityDefinitionValidator.Validate(WithField(
                new FieldDefinition { Key = "code", Type = FieldType.Text, Name = "编码", MaxLength = 2001 })),
            error => error.Rule == ValidationRules.FieldLengthOutOfRange);
    }

    [Fact]
    public void number精度超范围被拒()
    {
        Assert.Contains(EntityDefinitionValidator.Validate(WithField(
                new FieldDefinition { Key = "qty", Type = FieldType.Number, Name = "数量", Precision = 30 })),
            error => error.Rule == ValidationRules.FieldPrecisionOutOfRange);
    }

    [Fact]
    public void 单选必须提供选项()
    {
        Assert.Contains(EntityDefinitionValidator.Validate(WithField(
                new FieldDefinition { Key = "status", Type = FieldType.Select, Name = "状态" })),
            error => error.Rule == ValidationRules.FieldOptionsRequired);
    }

    [Fact]
    public void 多选选项值不能含双引号()
    {
        Assert.Contains(EntityDefinitionValidator.Validate(WithField(new FieldDefinition
            {
                Key = "tags",
                Type = FieldType.MultiSelect,
                Name = "标签",
                Options = [new FieldOption { Value = "a\"b", Label = "AB" }]
            })),
            error => error.Rule == ValidationRules.FieldOptionUnsafeValue);
    }

    [Fact]
    public void 关联字段必须指定目标实体()
    {
        Assert.Contains(EntityDefinitionValidator.Validate(WithField(
                new FieldDefinition { Key = "model_code", Type = FieldType.Relation, Name = "机种" })),
            error => error.Rule == ValidationRules.FieldRefEntityRequired);
    }

    [Theory]
    [InlineData("yes")]
    [InlineData("1x")]
    public void 布尔默认值只接受true和false(string defaultValue)
    {
        Assert.Contains(EntityDefinitionValidator.Validate(WithField(new FieldDefinition
            {
                Key = "enabled",
                Type = FieldType.Boolean,
                Name = "启用",
                DefaultValue = defaultValue
            })),
            error => error.Rule == ValidationRules.FieldDefaultInvalid);
    }

    [Fact]
    public void 百分比默认值超范围被拒()
    {
        Assert.Contains(EntityDefinitionValidator.Validate(WithField(new FieldDefinition
            {
                Key = "rate",
                Type = FieldType.Percent,
                Name = "比例",
                DefaultValue = "120"
            })),
            error => error.Rule == ValidationRules.FieldDefaultInvalid);
    }

    [Fact]
    public void 自动编号与附件不支持默认值()
    {
        Assert.Contains(EntityDefinitionValidator.Validate(WithField(new FieldDefinition
            {
                Key = "no",
                Type = FieldType.AutoNumber,
                Name = "单号",
                DefaultValue = "PD"
            })),
            error => error.Rule == ValidationRules.FieldDefaultNotAllowed);
    }

    [Fact]
    public void 特殊值只能用于对应类型()
    {
        Assert.Contains(EntityDefinitionValidator.Validate(WithField(new FieldDefinition
            {
                Key = "code",
                Type = FieldType.Text,
                Name = "编码",
                DefaultValue = "$now"
            })),
            error => error.Rule == ValidationRules.FieldDefaultUnsupported);
    }

    [Fact]
    public void CLOB字段不能建唯一或索引()
    {
        Assert.Contains(EntityDefinitionValidator.Validate(WithField(
                new FieldDefinition { Key = "body", Type = FieldType.LongText, Name = "正文", Unique = true })),
            error => error.Rule == ValidationRules.FieldIndexUnsupported);
    }

    [Fact]
    public void 范围模式只能是none或column()
    {
        var entity = new EntityDefinition
        {
            AppCode = "mes",
            Code = "sample",
            Name = "样例",
            ScopeMode = ScopeMode.Column,
            ScopeField = "ORG_ID",
            Fields = [new FieldDefinition { Key = "code", Type = FieldType.Text, Name = "编码" }]
        };

        Assert.Contains(EntityDefinitionValidator.Validate(entity),
            error => error.Rule == ValidationRules.ScopeFieldUnsupported);
    }

    [Fact]
    public void 视图不能引用不存在的字段()
    {
        var entity = new EntityDefinition
        {
            AppCode = "mes",
            Code = "sample",
            Name = "样例",
            Fields = [new FieldDefinition { Key = "code", Type = FieldType.Text, Name = "编码" }],
            Views =
            [
                new ViewDefinition
                {
                    Code = "default",
                    Name = "全部",
                    Columns = [new ViewColumn { Field = "nope" }]
                }
            ]
        };

        Assert.Contains(EntityDefinitionValidator.Validate(entity),
            error => error.Rule == ValidationRules.ViewColumnUnknownField);
    }

    [Fact]
    public void 视图不能按长文本排序()
    {
        var entity = new EntityDefinition
        {
            AppCode = "mes",
            Code = "sample",
            Name = "样例",
            Fields = [new FieldDefinition { Key = "body", Type = FieldType.LongText, Name = "正文" }],
            Views =
            [
                new ViewDefinition
                {
                    Code = "default",
                    Name = "全部",
                    DefaultSort = [new ViewSort { Field = "body" }]
                }
            ]
        };

        Assert.Contains(EntityDefinitionValidator.Validate(entity),
            error => error.Rule == ValidationRules.ViewSortNotSortable);
    }

    [Fact]
    public void 分页大小超上限被拒()
    {
        var entity = new EntityDefinition
        {
            AppCode = "mes",
            Code = "sample",
            Name = "样例",
            Fields = [new FieldDefinition { Key = "code", Type = FieldType.Text, Name = "编码" }],
            Views = [new ViewDefinition { Code = "default", Name = "全部", PageSize = 500 }]
        };

        Assert.Contains(EntityDefinitionValidator.Validate(entity),
            error => error.Rule == ValidationRules.ViewPageSizeOutOfRange);
    }
}
