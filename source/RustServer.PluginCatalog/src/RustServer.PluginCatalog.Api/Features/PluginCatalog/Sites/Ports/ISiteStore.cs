using RustServer.PluginCatalog.Api.Features.PluginCatalog.Sites.Domain;

namespace RustServer.PluginCatalog.Api.Features.PluginCatalog.Sites.Ports;

public interface ISiteStore
{
    Task<IReadOnlyList<Site>> ListAsync(CancellationToken ct);
    Task<Site?> FindAsync(Guid id, CancellationToken ct);
    void Add(Site site);
    Task RemoveAsync(Site site, CancellationToken ct);
    Task SaveAsync(CancellationToken ct);
}
