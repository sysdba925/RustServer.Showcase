using RustServer.PluginCatalog.Api.Features.PluginCatalog.Plugins.Domain;
using RustServer.PluginCatalog.Api.Features.PluginCatalog.Ports;

namespace RustServer.PluginCatalog.Api.Features.PluginCatalog.Plugins.Queries;

public sealed class GetPluginDetailsHandler(IPluginCatalogStore store)
{
    public async Task<CatalogResult<Plugin>> HandleAsync(Guid pluginId, CancellationToken cancellationToken)
    {
        var plugin = await store.GetPluginDetailsAsync(pluginId, cancellationToken);
        return plugin is null
            ? CatalogResult<Plugin>.Fail(CatalogFailures.PluginNotFound())
            : CatalogResult<Plugin>.Success(plugin);
    }
}
