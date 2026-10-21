using Microsoft.EntityFrameworkCore;
using RustServer.PluginCatalog.Api.Features.PluginCatalog.Organization.Domain;
using RustServer.PluginCatalog.Api.Features.PluginCatalog.Organization.Ports;

namespace RustServer.PluginCatalog.Api.Infrastructure.Persistence.PostgreSql;

public sealed class PostgreSqlOrganizationStore(PluginCatalogDbContext db) : IOrganizationStore
{
    public async Task<IReadOnlyList<Tag>> ListTagsAsync(CancellationToken ct) => await db.Set<Tag>().OrderBy(x => x.Name).ThenBy(x => x.Id).ToListAsync(ct);
    public async Task<IReadOnlyList<Folder>> ListFoldersAsync(CancellationToken ct) => await db.Set<Folder>().OrderBy(x => x.Name).ThenBy(x => x.Id).ToListAsync(ct);
    public Task<Tag?> FindTagAsync(Guid id, CancellationToken ct) => db.Set<Tag>().SingleOrDefaultAsync(x => x.Id == id, ct);
    public Task<Folder?> FindFolderAsync(Guid id, CancellationToken ct) => db.Set<Folder>().SingleOrDefaultAsync(x => x.Id == id, ct);
    public void AddTag(Tag tag) => db.Add(tag);
    public void AddFolder(Folder folder) => db.Add(folder);
    public void RemoveTag(Tag tag) => db.Remove(tag);
    public void RemoveFolder(Folder folder) => db.Remove(folder);
    public Task SaveAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}
