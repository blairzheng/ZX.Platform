using Microsoft.EntityFrameworkCore;
using ZX.Platform.LowCode.Application.Metadata;
using ZX.Platform.LowCode.Domain.Metadata;

namespace ZX.Platform.LowCode.Infrastructure.Persistence;

/// <summary>元数据仓储的 EF 实现。只管 6 张静态表，业务表一行都不碰。</summary>
public sealed class EfMetadataStore(LowCodeDbContext db) : IMetadataStore
{
    public Task<MetaEntityRecord?> FindEntityAsync(
        string appCode, string code, CancellationToken cancellationToken) =>
        db.Entities.FirstOrDefaultAsync(
            item => item.AppCode == appCode && item.Code == code && !item.IsDeleted,
            cancellationToken);

    public async Task<IReadOnlyList<MetaEntityRecord>> ListEntitiesAsync(
        string appCode, CancellationToken cancellationToken) =>
        await db.Entities
            .AsNoTracking()
            .Where(item => item.AppCode == appCode && !item.IsDeleted)
            .OrderBy(item => item.Code)
            .ToListAsync(cancellationToken);

    public Task AddEntityAsync(MetaEntityRecord record, CancellationToken cancellationToken)
    {
        db.Entities.Add(record);
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<MetaVersionRecord>> ListVersionsAsync(
        string ownerType, string ownerCode, CancellationToken cancellationToken) =>
        await db.Versions
            .AsNoTracking()
            .Where(item => item.OwnerType == ownerType && item.OwnerCode == ownerCode)
            .OrderByDescending(item => item.Version)
            .ToListAsync(cancellationToken);

    public Task AddVersionAsync(MetaVersionRecord record, CancellationToken cancellationToken)
    {
        db.Versions.Add(record);
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<SchemaChangeRecord>> ListPendingChangesAsync(
        string entityCode, CancellationToken cancellationToken) =>
        await db.SchemaChanges
            .AsNoTracking()
            .Where(item => item.EntityCode == entityCode && item.Status != SchemaChangeStatus.Applied)
            .OrderBy(item => item.CreatedAtUtc)
            .ToListAsync(cancellationToken);

    public Task<SchemaChangeRecord?> FindChangeAsync(Guid id, CancellationToken cancellationToken) =>
        db.SchemaChanges.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);

    public Task AddChangesAsync(IEnumerable<SchemaChangeRecord> changes, CancellationToken cancellationToken)
    {
        db.SchemaChanges.AddRange(changes);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}
