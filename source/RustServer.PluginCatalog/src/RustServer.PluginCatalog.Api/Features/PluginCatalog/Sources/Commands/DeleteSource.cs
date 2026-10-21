using RustServer.PluginCatalog.Api.Features.PluginCatalog.Ports;

namespace RustServer.PluginCatalog.Api.Features.PluginCatalog.Sources.Commands;

public sealed class DeleteSourceHandler(IPluginCatalogStore store)
{
    public async Task<CatalogResult<bool>> HandleAsync(
        Guid pluginId,
        Guid sourceId,
        CancellationToken cancellationToken)
    {
        var source = await store.GetSourceForUpdateAsync(pluginId, sourceId, cancellationToken);
        if (source is null)
        {
            return CatalogResult<bool>.Fail(CatalogFailures.SourceNotFound());
        }

        store.RemoveSource(source);
        await store.SaveChangesAsync(cancellationToken);
        return CatalogResult<bool>.Success(true);
    }
}
