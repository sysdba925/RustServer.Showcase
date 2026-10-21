using RustServer.PluginCatalog.Api.Features.PluginCatalog.Ports;

namespace RustServer.PluginCatalog.Api.Features.PluginCatalog.Plugins.Commands;

public sealed class DeletePluginHandler(IPluginCatalogStore store, IPluginMediaStorage storage, ILogger<DeletePluginHandler> logger)
{
    public async Task<CatalogResult<bool>> HandleAsync(Guid pluginId, CancellationToken cancellationToken)
    {
        var media = await store.DeletePluginAsync(pluginId, cancellationToken);
        if (media is null)
        {
            return CatalogResult<bool>.Fail(CatalogFailures.PluginNotFound());
        }

        foreach (var item in media.Where(item => item.StorageKey is not null))
        {
            try { await storage.DeleteAsync(item.StorageKey!, CancellationToken.None); }
            catch (Exception exception) when (exception is PluginMediaStorageException or IOException or UnauthorizedAccessException)
            { logger.LogWarning(exception, "Could not remove orphaned files for deleted media {MediaId}.", item.Id); }
        }
        return CatalogResult<bool>.Success(true);
    }
}
