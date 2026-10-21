using RustServer.PluginCatalog.Api.Features.PluginCatalog.Plugins.Domain;
using RustServer.PluginCatalog.Api.Features.PluginCatalog.Ports;

namespace RustServer.PluginCatalog.Api.Features.PluginCatalog.Media.Queries;

public sealed class GetPluginMediaHandler(IPluginCatalogStore store)
{
    public async Task<CatalogResult<IReadOnlyList<PluginMedia>>> HandleAsync(
        Guid pluginId,
        CancellationToken cancellationToken)
    {
        if (!await store.PluginExistsAsync(pluginId, cancellationToken))
        {
            return CatalogResult<IReadOnlyList<PluginMedia>>.Fail(CatalogFailures.PluginNotFound());
        }

        var media = await store.GetMediaAsync(pluginId, cancellationToken);
        return CatalogResult<IReadOnlyList<PluginMedia>>.Success(media);
    }
}
