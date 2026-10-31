using System.Globalization;
using System.Text.RegularExpressions;
using ZX.Platform.LowCode.Application.Metadata;
using ZX.Platform.LowCode.Application.Runtime;
using ZX.Platform.LowCode.Application.Schema;
using ZX.Platform.LowCode.Domain;
using ZX.Platform.LowCode.Domain.Metadata;

namespace ZX.Platform.Identity.AuthChecks;

/// <summary>
/// 低代码四个出口的内存替身。
///
/// 只替"数据在哪"，不替"逻辑"：MetadataService / QueryEngine / RecordEngine /
/// SchemaChangeService 用的都是真实现，这样 HTTP 检查测的才是真正会跑的那条链路。
/// </summary>
internal sealed class FakeMetadataStore : IMetadataStore
{
    public List<MetaEntityRecord> Entities { get; } = [];
    public List<MetaVersionRecord> Versions { get; } = [];
    public List<SchemaChangeRecord> Changes { get; } = [];

    public Task<MetaEntityRecord?> FindEntityAsync(string appCode, string code, CancellationToken cancellationToken) =>
        Task.FromResult(Entities.FirstOrDefault(
            item => item.AppCode == appCode && item.Code == code && !item.IsDeleted));

    public Task<IReadOnlyList<MetaEntityRecord>> ListEntitiesAsync(
        string appCode, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<MetaEntityRecord>>(Entities
            .Where(item => item.AppCode == appCode && !item.IsDeleted)
            .OrderBy(item => item.Code)
            .ToList());

    public Task AddEntityAsync(MetaEntityRecord record, CancellationToken cancellationToken)
    {
        Entities.Add(record);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<MetaVersionRecord>> ListVersionsAsync(
        string ownerType, string ownerCode, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<MetaVersionRecord>>(Versions
            .Where(item => item.OwnerType == ownerType && item.OwnerCode == ownerCode)
            .OrderByDescending(item => item.Version)
            .ToList());

    public Task AddVersionAsync(MetaVersionRecord record, CancellationToken cancellationToken)
    {
        Versions.Add(record);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<SchemaChangeRecord>> ListPendingChangesAsync(
        string entityCode, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<SchemaChangeRecord>>(Changes
            .Where(item => item.EntityCode == entityCode && item.Status != SchemaChangeStatus.Applied)
            .OrderBy(item => item.CreatedAtUtc)
            .ToList());

    public Task<SchemaChangeRecord?> FindChangeAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Changes.FirstOrDefault(item => item.Id == id));

    public Task AddChangesAsync(IEnumerable<SchemaChangeRecord> changes, CancellationToken cancellationToken)
    {
        Changes.AddRange(changes);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

internal sealed class FakeSchemaInspector : ISchemaInspector
{
    private readonly Dictionary<string, HashSet<string>> _tables = new(StringComparer.OrdinalIgnoreCase);

    public Task<TableShape> InspectAsync(string tableName, CancellationToken cancellationToken) =>
        Task.FromResult(_tables.TryGetValue(tableName, out var columns)
            ? new TableShape(true, [.. columns.Select(name => new TableColumn(name, string.Empty))])
            : TableShape.Missing);

    public void CreateTable(string tableName, IEnumerable<string> columns) =>
        _tables[tableName] = new HashSet<string>(columns, StringComparer.OrdinalIgnoreCase);

    public void AddColumn(string tableName, string column)
    {
        if (_tables.TryGetValue(tableName, out var set)) set.Add(column);
    }
}

/// <summary>
/// 假的 DDL 执行器：从生成的语句里认出建表 / 加列，回写到 <see cref="FakeSchemaInspector"/>。
/// 这样"执行 → 核对 → 放行发布"的完整回路能在内存里跑通。
/// </summary>
internal sealed partial class FakeSchemaChangeExecutor(FakeSchemaInspector inspector) : ISchemaChangeExecutor
{
    public Task ExecuteAsync(IReadOnlyList<string> statements, CancellationToken cancellationToken)
    {
        foreach (var statement in statements)
        {
            var create = CreateTablePattern().Match(statement);
            if (create.Success)
            {
                inspector.CreateTable(
                    create.Groups["table"].Value,
                    ColumnPattern().Matches(create.Groups["body"].Value)
                        .Select(match => match.Groups["column"].Value));
                continue;
            }

            var alter = AddColumnPattern().Match(statement);
            if (alter.Success)
                inspector.AddColumn(alter.Groups["table"].Value, alter.Groups["column"].Value);
        }

        return Task.CompletedTask;
    }

    [GeneratedRegex("^CREATE TABLE \"(?<table>[^\"]+)\" \\((?<body>.*)\\)$", RegexOptions.Singleline)]
    private static partial Regex CreateTablePattern();

    [GeneratedRegex("\n\\s+\"(?<column>[^\"]+)\" ")]
    private static partial Regex ColumnPattern();

    [GeneratedRegex("^ALTER TABLE \"(?<table>[^\"]+)\" ADD \\(\"(?<column>[^\"]+)\"")]
    private static partial Regex AddColumnPattern();
}

/// <summary>
/// 内存业务表。行以**列名**为键返回，和 Dapper 网关一致，
/// 这样 RecordValueMapper 的列名 → 字段 key 映射也会被一起验证。
/// </summary>
internal sealed class FakeRecordGateway : IRecordGateway
{
    private sealed class Row
    {
        public Guid Id { get; init; }
        public int Revision { get; set; } = 1;
        public bool Deleted { get; set; }
        public Dictionary<string, object?> Columns { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private readonly Dictionary<string, List<Row>> _tables = new(StringComparer.OrdinalIgnoreCase);

    public Task<RecordPage> QueryAsync(QuerySpec spec, CancellationToken cancellationToken)
    {
        var rows = _tables.TryGetValue(spec.Entity.TableName, out var list) ? list : [];
        IEnumerable<Row> matched = rows.Where(row => !row.Deleted);

        if (spec.Id is Guid id) matched = matched.Where(row => row.Id == id);

        foreach (var filter in spec.Filters)
        {
            var column = spec.Entity.FindField(filter.Field)?.ColumnName
                         ?? filter.Field.ToUpperInvariant();

            matched = matched.Where(row => row.Columns.TryGetValue(column, out var value)
                && string.Equals(
                    Convert.ToString(value, CultureInfo.InvariantCulture), filter.Value, StringComparison.Ordinal));
        }

        var all = matched.ToList();
        var take = spec.Take <= 0 ? ViewDefinition.DefaultPageSize : spec.Take;

        var page = all.Skip(spec.Skip).Take(take)
            .Select(row => (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>(
                row.Columns, StringComparer.OrdinalIgnoreCase)
            {
                [ReservedColumns.Id] = row.Id,
                [ReservedColumns.Revision] = row.Revision
            })
            .ToList();

        return Task.FromResult(new RecordPage(page, all.Count, spec.Skip, take));
    }

    public Task CreateAsync(
        EntityDefinition entity,
        Guid id,
        IReadOnlyDictionary<string, object?> values,
        RecordScope scope,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var row = new Row { Id = id };
        foreach (var field in entity.ActiveFields)
        {
            row.Columns[field.ColumnName] = values.TryGetValue(field.Key, out var value)
                ? Normalize(field, value)
                : field.HasDefault ? field.DefaultValue : null;
        }

        Table(entity).Add(row);
        return Task.CompletedTask;
    }

    public Task<bool> UpdateAsync(
        EntityDefinition entity,
        Guid id,
        int revision,
        IReadOnlyDictionary<string, object?> values,
        RecordScope scope,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var row = Table(entity).FirstOrDefault(item => item.Id == id && !item.Deleted && item.Revision == revision);
        if (row is null) return Task.FromResult(false);

        foreach (var (key, value) in values)
        {
            var field = entity.FindField(key);
            if (field is null) continue;
            row.Columns[field.ColumnName] = Normalize(field, value);
        }

        row.Revision++;
        return Task.FromResult(true);
    }

    public Task<bool> DeleteAsync(
        EntityDefinition entity,
        Guid id,
        int revision,
        RecordScope scope,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var row = Table(entity).FirstOrDefault(item => item.Id == id && !item.Deleted && item.Revision == revision);
        if (row is null) return Task.FromResult(false);

        row.Deleted = true;
        row.Revision++;
        return Task.FromResult(true);
    }

    private List<Row> Table(EntityDefinition entity)
    {
        if (!_tables.TryGetValue(entity.TableName, out var list))
            _tables[entity.TableName] = list = [];

        return list;
    }

    /// <summary>按 Oracle 存进去的样子放：boolean 是 0/1，数字是 decimal。</summary>
    private static object? Normalize(FieldDefinition field, object? value) => field.Type switch
    {
        FieldType.Boolean => value is true or "true" ? 1m : 0m,
        FieldType.Number or FieldType.Amount or FieldType.Percent when value is string text
            => decimal.Parse(text, CultureInfo.InvariantCulture),
        _ => value
    };
}
