using ZX.Platform.LowCode.Application.Metadata;
using ZX.Platform.LowCode.Domain.Metadata;

namespace ZX.Platform.LowCode.Tests;

/// <summary>内存仓储，让 MetadataService 可以脱离数据库测试。</summary>
internal sealed class InMemoryMetadataStore : IMetadataStore
{
    public List<MetaEntityRecord> Entities { get; } = [];
    public List<MetaVersionRecord> Versions { get; } = [];
    public List<SchemaChangeRecord> Changes { get; } = [];
    public int SaveCount { get; private set; }

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

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        SaveCount++;
        return Task.CompletedTask;
    }
}
