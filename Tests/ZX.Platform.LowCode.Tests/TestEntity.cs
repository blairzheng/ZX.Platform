using ZX.Platform.LowCode.Domain;

namespace ZX.Platform.LowCode.Tests;

internal static class TestEntity
{
    public static EntityDefinition Station(bool sbuScope = false) => new()
    {
        AppCode = "mes",
        Code = "station",
        Name = "站点",
        ScopeMode = sbuScope ? ScopeMode.Column : ScopeMode.None,
        Fields =
        [
            new FieldDefinition
            {
                Key = "code",
                Type = FieldType.Text,
                Name = "站点编码",
                Required = true,
                MaxLength = 64,
                Unique = true,
                Sort = 10
            },
            new FieldDefinition
            {
                Key = "name",
                Type = FieldType.Text,
                Name = "站点名称",
                Required = true,
                Sort = 20
            },
            new FieldDefinition
            {
                Key = "is_enabled",
                Type = FieldType.Boolean,
                Name = "启用",
                Required = true,
                DefaultValue = "true",
                Sort = 30
            }
        ]
    };

    /// <summary>在站点定义上追加字段，用于"发布后再加列"的场景。</summary>
    public static EntityDefinition StationWith(params FieldDefinition[] extraFields) => new()
    {
        AppCode = "mes",
        Code = "station",
        Name = "站点",
        Fields = [.. Station().Fields, .. extraFields]
    };

    /// <summary>把 name 字段换成另一种类型，用于验证"类型不可修改"。</summary>
    public static EntityDefinition StationWithNameType(FieldType type) => new()
    {
        AppCode = "mes",
        Code = "station",
        Name = "站点",
        Fields =
        [
            Station().Fields[0],
            new FieldDefinition
            {
                Key = "name",
                Type = type,
                Name = "站点名称",
                Required = true,
                Sort = 20
            },
            Station().Fields[2]
        ]
    };


    /// <summary>盘点单：主表，带 SBU 范围与关联字段。</summary>
    public static EntityDefinition Stocktake() => new()
    {
        AppCode = "mes",
        Code = "stocktake",
        Name = "盘点单",
        ScopeMode = ScopeMode.Column,
        Fields =
        [
            new FieldDefinition
            {
                Key = "model_code",
                Type = FieldType.Relation,
                Name = "机种",
                Required = true,
                RefEntity = "model",
                Sort = 10
            },
            new FieldDefinition
            {
                Key = "stocktake_date",
                Type = FieldType.Date,
                Name = "盘点日期",
                Required = true,
                DefaultValue = "$now",
                Sort = 20
            },
            new FieldDefinition
            {
                Key = "remark",
                Type = FieldType.LongText,
                Name = "备注",
                Sort = 30
            }
        ]
    };
}
