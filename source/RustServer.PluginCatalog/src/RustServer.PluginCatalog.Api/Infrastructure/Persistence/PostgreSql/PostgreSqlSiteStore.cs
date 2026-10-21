using Microsoft.EntityFrameworkCore;
using RustServer.PluginCatalog.Api.Features.PluginCatalog.Sites.Domain;
using RustServer.PluginCatalog.Api.Features.PluginCatalog.Sites.Ports;

namespace RustServer.PluginCatalog.Api.Infrastructure.Persistence.PostgreSql;

public sealed class PostgreSqlSiteStore(PluginCatalogDbContext db) : ISiteStore
{
    public async Task<IReadOnlyList<Site>> ListAsync(CancellationToken ct) =>
        await db.Sites.AsNoTracking().OrderBy(x => x.Name).ThenBy(x => x.Id).ToListAsync(ct);
    public Task<Site?> FindAsync(Guid id, CancellationToken ct) => db.Sites.SingleOrDefaultAsync(x => x.Id == id, ct);
    public void Add(Site site) => db.Sites.Add(site);
    public async Task RemoveAsync(Site site, CancellationToken ct)
    {
        await db.PluginSources.Where(source => source.SiteId == site.Id).LoadAsync(ct);
        db.Sites.Remove(site);
    }
    public async Task SaveAsync(CancellationToken ct) => await db.SaveChangesAsync(ct);
}
