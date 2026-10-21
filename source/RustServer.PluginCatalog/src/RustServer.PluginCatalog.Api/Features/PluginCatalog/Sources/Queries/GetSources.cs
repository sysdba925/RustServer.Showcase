using RustServer.PluginCatalog.Api.Features.PluginCatalog.Plugins.Domain;
using RustServer.PluginCatalog.Api.Features.PluginCatalog.Ports;

namespace RustServer.PluginCatalog.Api.Features.PluginCatalog.Sources.Queries;

public sealed class GetSourcesHandler(IPluginCatalogStore store)
{
    public async Task<CatalogResult<IReadOnlyList<PluginSource>>> HandleAsync(
        Guid pluginId,
        CancellationToken cancellationToken)
    {
        if (!await store.PluginExistsAsync(pluginId, cancellationToken))
        {
            return CatalogResult<IReadOnlyList<PluginSource>>.Fail(CatalogFailures.PluginNotFound());
        }

        var sources = await store.GetSourcesAsync(pluginId, cancellationToken);
        return CatalogResult<IReadOnlyList<PluginSource>>.Success(sources);
    }
}
