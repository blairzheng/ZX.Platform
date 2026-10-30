namespace ZX.Platform.LowCode.Domain;

/// <summary>元数据校验结果。Path 定位到 JSON 里的位置，Rule 供前端与测试断言。</summary>
public sealed record ValidationError(string Path, string Rule, string Message);

public static class ValidationRules
{
    public const string SchemaVersionUnsupported = "schema.version.unsupported";
    public const string CodeInvalid = "code.invalid";
    public const string NameMissing = "name.missing";
    public const string TableNameTooLong = "table.name.tooLong";
    public const string FieldCountOutOfRange = "field.count.outOfRange";
    public const string FieldKeyDuplicate = "field.key.duplicate";
    public const string FieldKeyReserved = "field.key.reserved";
    public const string FieldNameMissing = "field.name.missing";
    public const string FieldLengthOutOfRange = "field.length.outOfRange";
    public const string FieldPrecisionOutOfRange = "field.precision.outOfRange";
    public const string FieldOptionsRequired = "field.options.required";
    public const string FieldOptionDuplicate = "field.options.duplicate";
    public const string FieldOptionTooLong = "field.options.tooLong";
    public const string FieldOptionUnsafeValue = "field.options.unsafeValue";
    public const string FieldRefEntityRequired = "field.refEntity.required";
    public const string FieldIndexUnsupported = "field.index.unsupported";
    public const string FieldDefaultNotAllowed = "field.default.notAllowed";
    public const string FieldDefaultUnsupported = "field.default.unsupported";
    public const string FieldDefaultInvalid = "field.default.invalid";
    public const string ScopeFieldUnsupported = "scope.field.unsupported";
    public const string ViewCodeDuplicate = "view.code.duplicate";
    public const string ViewColumnUnknownField = "view.column.unknownField";
    public const string ViewSortUnknownField = "view.sort.unknownField";
    public const string ViewSortNotSortable = "view.sort.notSortable";
    public const string ViewFilterNotFilterable = "view.filter.notFilterable";
    public const string ViewPageSizeOutOfRange = "view.pageSize.outOfRange";
}

/// <summary>
/// 实体定义校验器。所有规则集中在这里，设计器与手写 JSON 走同一条路径。
/// 校验失败一律 HTTP 400 / 600001，具体原因看 Rule。
/// </summary>
public static class EntityDefinitionValidator
{
    public static IReadOnlyList<ValidationError> Validate(EntityDefinition entity)
    {
        var errors = new List<ValidationError>();

        if (entity.SchemaVersion != EntityDefinition.CurrentSchemaVersion)
            errors.Add(new ValidationError("schemaVersion", ValidationRules.SchemaVersionUnsupported,
                $"只支持 schemaVersion = {EntityDefinition.CurrentSchemaVersion}。"));

        if (!Naming.IsValidCode(entity.AppCode))
            errors.Add(new ValidationError("appCode", ValidationRules.CodeInvalid,
                "应用编码必须以小写字母开头，只含小写字母、数字和下划线，最长 30 字符。"));

        if (!Naming.IsValidCode(entity.Code))
            errors.Add(new ValidationError("code", ValidationRules.CodeInvalid,
                "实体编码必须以小写字母开头，只含小写字母、数字和下划线，最长 30 字符。"));

        if (string.IsNullOrWhiteSpace(entity.Name))
            errors.Add(new ValidationError("name", ValidationRules.NameMissing, "实体名称不能为空。"));

        if (Naming.IsValidCode(entity.AppCode) && Naming.IsValidCode(entity.Code))
        {
            var tableName = entity.TableName;
            if (!Naming.IsValidTableName(tableName))
                errors.Add(new ValidationError("code", ValidationRules.TableNameTooLong,
                    $"物理表名 {tableName} 超过 {Naming.MaxIdentifierLength} 字符，请缩短应用或实体编码。"));
        }

        ValidateScope(entity, errors);
        ValidateFields(entity, errors);
        ValidateViews(entity, errors);

        return errors;
    }

    private static void ValidateScope(EntityDefinition entity, List<ValidationError> errors)
    {
        if (entity.ScopeMode == ScopeMode.None)
        {
            if (!string.IsNullOrWhiteSpace(entity.ScopeField) &&
                !string.Equals(entity.ScopeField, ReservedColumns.SbuId, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add(new ValidationError("scopeField", ValidationRules.ScopeFieldUnsupported,
                    "scopeMode = none 时不应指定 scopeField。"));
            }

            return;
        }

        var field = string.IsNullOrWhiteSpace(entity.ScopeField) ? ReservedColumns.SbuId : entity.ScopeField;
        if (!string.Equals(field, ReservedColumns.SbuId, StringComparison.OrdinalIgnoreCase))
            errors.Add(new ValidationError("scopeField", ValidationRules.ScopeFieldUnsupported,
                "scopeMode = column 时 scopeField 只允许 SBU_ID。"));
    }

    private static void ValidateFields(EntityDefinition entity, List<ValidationError> errors)
    {
        if (entity.Fields.Count is < 1 or > EntityDefinition.MaxFieldCount)
            errors.Add(new ValidationError("fields", ValidationRules.FieldCountOutOfRange,
                $"字段数必须在 1–{EntityDefinition.MaxFieldCount} 之间，当前 {entity.Fields.Count}。"));

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < entity.Fields.Count; index++)
        {
            var field = entity.Fields[index];
            var path = $"fields[{index}]";

            if (!Naming.IsValidCode(field.Key))
                errors.Add(new ValidationError($"{path}.key", ValidationRules.CodeInvalid,
                    $"字段 key「{field.Key}」不合法：小写字母开头，只含小写字母、数字和下划线，最长 30 字符。"));
            else if (!seen.Add(field.Key))
                errors.Add(new ValidationError($"{path}.key", ValidationRules.FieldKeyDuplicate,
                    $"字段 key「{field.Key}」重复。"));
            else if (ReservedColumns.IsReserved(field.Key))
                errors.Add(new ValidationError($"{path}.key", ValidationRules.FieldKeyReserved,
                    $"字段 key「{field.Key}」与保留列冲突。"));

            if (string.IsNullOrWhiteSpace(field.Name))
                errors.Add(new ValidationError($"{path}.name", ValidationRules.FieldNameMissing,
                    $"字段「{field.Key}」缺少名称。"));

            ValidateLength(field, path, errors);
            ValidateOptions(field, path, errors);
            ValidateReference(field, path, errors);
            ValidateDefault(field, path, errors);
            ValidateIndexability(field, path, errors);
        }
    }

    private static void ValidateIndexability(FieldDefinition field, string path, List<ValidationError> errors)
    {
        // CLOB 不能建索引，也不能出现在虚拟列表达式里。
        if ((field.Unique || field.Indexed) && !FieldTypeRules.Indexable(field.Type))
            errors.Add(new ValidationError($"{path}.indexed", ValidationRules.FieldIndexUnsupported,
                $"{field.Type.ToWireName()} 字段「{field.Key}」不能建索引或唯一约束。"));
    }

    private static void ValidateLength(FieldDefinition field, string path, List<ValidationError> errors)
    {
        switch (field.Type)
        {
            case FieldType.Text when field.MaxLength is < 1 or > FieldDefinition.MaxTextLength:
                errors.Add(new ValidationError($"{path}.maxLength", ValidationRules.FieldLengthOutOfRange,
                    $"text 字段长度必须在 1–{FieldDefinition.MaxTextLength} 之间。"));
                break;

            case FieldType.LongText when field.MaxLength is > FieldDefinition.MaxLongTextLength:
                errors.Add(new ValidationError($"{path}.maxLength", ValidationRules.FieldLengthOutOfRange,
                    $"longtext 字段长度不得超过 {FieldDefinition.MaxLongTextLength}。"));
                break;

            case FieldType.Number when field.Precision is < 1 or > FieldDefinition.MaxPrecision:
                errors.Add(new ValidationError($"{path}.precision", ValidationRules.FieldPrecisionOutOfRange,
                    $"number 精度必须在 1–{FieldDefinition.MaxPrecision} 之间。"));
                break;
        }
    }

    private static void ValidateOptions(FieldDefinition field, string path, List<ValidationError> errors)
    {
        if (field.Type is not (FieldType.Select or FieldType.MultiSelect)) return;

        if (field.Options.Count == 0)
        {
            errors.Add(new ValidationError($"{path}.options", ValidationRules.FieldOptionsRequired,
                $"{field.Type.ToWireName()} 字段必须提供 options。"));
            return;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < field.Options.Count; index++)
        {
            var option = field.Options[index];
            if (!seen.Add(option.Value))
                errors.Add(new ValidationError($"{path}.options[{index}].value", ValidationRules.FieldOptionDuplicate,
                    $"选项值「{option.Value}」重复。"));

            if (option.Value.Length > FieldDefinition.MaxOptionValueLength)
                errors.Add(new ValidationError($"{path}.options[{index}].value", ValidationRules.FieldOptionTooLong,
                    $"选项值长度不得超过 {FieldDefinition.MaxOptionValueLength}。"));

            // multiselect 的 contains 是 LIKE '%"value"%'，值里带引号会破坏匹配。
            if (field.Type == FieldType.MultiSelect && option.Value.Contains('"'))
                errors.Add(new ValidationError($"{path}.options[{index}].value", ValidationRules.FieldOptionUnsafeValue,
                    "multiselect 的选项值不能包含双引号。"));
        }
    }

    private static void ValidateReference(FieldDefinition field, string path, List<ValidationError> errors)
    {
        if (field.Type != FieldType.Relation) return;

        if (!Naming.IsValidCode(field.RefEntity))
            errors.Add(new ValidationError($"{path}.refEntity", ValidationRules.FieldRefEntityRequired,
                "relation 字段必须指定合法的 refEntity。"));

        if (!string.IsNullOrWhiteSpace(field.RefDisplayField) && !Naming.IsValidCode(field.RefDisplayField))
            errors.Add(new ValidationError($"{path}.refDisplayField", ValidationRules.CodeInvalid,
                "refDisplayField 必须是合法字段 key。"));
    }

    private static void ValidateDefault(FieldDefinition field, string path, List<ValidationError> errors)
    {
        if (!field.HasDefault) return;

        var raw = field.DefaultValue!;

        if (field.Type is FieldType.AutoNumber or FieldType.Attachment)
        {
            errors.Add(new ValidationError($"{path}.defaultValue", ValidationRules.FieldDefaultNotAllowed,
                $"{field.Type.ToWireName()} 字段不支持默认值。"));
            return;
        }

        if (FieldValueParser.IsSpecial(raw))
        {
            if (!FieldValueParser.IsAllowedSpecial(field.Type, raw))
                errors.Add(new ValidationError($"{path}.defaultValue", ValidationRules.FieldDefaultUnsupported,
                    $"特殊值 {raw} 不能用于 {field.Type.ToWireName()} 字段。"));
            return;
        }

        if (!FieldValueParser.TryParseValue(field.Type, raw, field.Options, out _))
            errors.Add(new ValidationError($"{path}.defaultValue", ValidationRules.FieldDefaultInvalid,
                $"默认值「{raw}」无法解析为 {field.Type.ToWireName()}。"));
    }

    private static void ValidateViews(EntityDefinition entity, List<ValidationError> errors)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < entity.Views.Count; index++)
        {
            var view = entity.Views[index];
            var path = $"views[{index}]";

            if (!Naming.IsValidCode(view.Code))
                errors.Add(new ValidationError($"{path}.code", ValidationRules.CodeInvalid,
                    $"视图编码「{view.Code}」不合法。"));
            else if (!seen.Add(view.Code))
                errors.Add(new ValidationError($"{path}.code", ValidationRules.ViewCodeDuplicate,
                    $"视图编码「{view.Code}」重复。"));

            if (view.PageSize is < 1 or > ViewDefinition.MaxPageSize)
                errors.Add(new ValidationError($"{path}.pageSize", ValidationRules.ViewPageSizeOutOfRange,
                    $"分页大小必须在 1–{ViewDefinition.MaxPageSize} 之间。"));

            foreach (var column in view.Columns)
            {
                var field = entity.FindField(column.Field);
                if (field is null)
                    errors.Add(new ValidationError($"{path}.columns", ValidationRules.ViewColumnUnknownField,
                        $"视图引用了不存在的字段「{column.Field}」。"));
                else if (field.Disabled)
                    errors.Add(new ValidationError($"{path}.columns", ValidationRules.ViewColumnUnknownField,
                        $"视图引用了已停用字段「{column.Field}」。"));
            }

            foreach (var sort in view.DefaultSort)
            {
                var field = entity.FindField(sort.Field);
                if (field is null)
                    errors.Add(new ValidationError($"{path}.defaultSort", ValidationRules.ViewSortUnknownField,
                        $"排序引用了不存在的字段「{sort.Field}」。"));
                else if (!FieldTypeRules.Sortable(field.Type))
                    errors.Add(new ValidationError($"{path}.defaultSort", ValidationRules.ViewSortNotSortable,
                        $"{field.Type.ToWireName()} 字段不可排序。"));
            }

            foreach (var filterField in view.FilterFields)
            {
                var field = entity.FindField(filterField);
                if (field is null)
                    errors.Add(new ValidationError($"{path}.filters", ValidationRules.ViewSortUnknownField,
                        $"筛选项引用了不存在的字段「{filterField}」。"));
                else if (!FieldTypeRules.SupportsAnyFilter(field.Type))
                    errors.Add(new ValidationError($"{path}.filters", ValidationRules.ViewFilterNotFilterable,
                        $"{field.Type.ToWireName()} 字段不可筛选。"));
            }
        }
    }
}
