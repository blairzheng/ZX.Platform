namespace ZX.Platform.LowCode.Domain;

/// <summary>
/// 已发布定义 → 新草稿之间**不允许**发生的变更。
///
/// 这里只管"语义上不可逆"的部分（表名、字段类型）。"物理上还缺哪些列"不在这里判断——
/// 那件事只能问数据库，见 ISchemaInspector。
/// </summary>
public static class SchemaCompatibility
{
    /// <summary>
    /// 发布前调用。published 为 null（第一次发布）时什么都不用查。
    ///
    /// 为什么改类型必须禁止而不是尽力转换：一旦允许，同一列在不同历史行上就是两种语义，
    /// 元数据里却只留一条记录，之后没有任何办法说清"这行到底是按旧类型还是新类型存的"。
    /// 正确做法是新建字段、写数据迁移脚本、停用旧字段。
    /// </summary>
    public static void EnsureCompatible(EntityDefinition published, EntityDefinition draft)
    {
        if (!string.Equals(published.TableName, draft.TableName, StringComparison.Ordinal))
            throw new LowCodeException(LowCodeErrorCodes.ValidationFailed,
                $"物理表名不能变更（{published.TableName} → {draft.TableName}）。");

        foreach (var field in draft.ActiveFields)
        {
            var existing = published.FindField(field.Key);
            if (existing is null) continue;

            if (existing.Type != field.Type)
                throw new LowCodeException(LowCodeErrorCodes.ValidationFailed,
                    $"字段「{field.Key}」的类型不能修改"
                    + $"（{existing.Type.ToWireName()} → {field.Type.ToWireName()}），请新建字段后迁移数据。");
        }
    }
}
