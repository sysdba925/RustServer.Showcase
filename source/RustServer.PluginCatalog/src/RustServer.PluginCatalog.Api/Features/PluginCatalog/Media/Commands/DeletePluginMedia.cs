using RustServer.PluginCatalog.Api.Features.PluginCatalog.Ports;

namespace RustServer.PluginCatalog.Api.Features.PluginCatalog.Media.Commands;

public sealed class DeletePluginMediaHandler(
    IPluginCatalogStore store,
    IPluginMediaStorage storage,
    ILogger<DeletePluginMediaHandler> logger)
{
    public async Task<CatalogResult<bool>> HandleAsync(
        Guid pluginId,
        Guid mediaId,
        CancellationToken cancellationToken)
    {
        var plugin = await store.GetPluginForUpdateAsync(pluginId, cancellationToken);
        if (plugin is null)
        {
            return CatalogResult<bool>.Fail(CatalogFailures.MediaNotFound());
        }

        var media = plugin.Media.SingleOrDefault(item => item.Id == mediaId);
        if (media is null)
        {
            return CatalogResult<bool>.Fail(CatalogFailures.MediaNotFound());
        }

        var storageKey = media.StorageKey;
        if (plugin.IconMediaId == media.Id)
        {
            plugin.SetIcon(null);
        }

        store.RemoveMedia(media);
        await store.SaveChangesAsync(cancellationToken);

        if (storageKey is not null)
        {
            try
            {
                await storage.DeleteAsync(storageKey, CancellationToken.None);
            }
            catch (Exception exception) when (exception is PluginMediaStorageException or IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(exception, "Could not remove orphaned files for deleted media {MediaId}.", mediaId);
            }
        }

        return CatalogResult<bool>.Success(true);
    }
}
