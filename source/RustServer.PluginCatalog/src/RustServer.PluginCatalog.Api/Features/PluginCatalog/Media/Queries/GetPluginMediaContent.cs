using RustServer.PluginCatalog.Api.Features.PluginCatalog.Ports;

namespace RustServer.PluginCatalog.Api.Features.PluginCatalog.Media.Queries;

public sealed class GetPluginMediaContentHandler(
    IPluginCatalogStore store,
    IPluginMediaStorage storage)
{
    public async Task<CatalogResult<PluginMediaContent>> HandleAsync(
        Guid pluginId,
        Guid mediaId,
        PluginMediaStorageVariant variant,
        CancellationToken cancellationToken)
    {
        var media = await store.GetMediaForReadAsync(pluginId, mediaId, cancellationToken);
        if (media is null)
        {
            return CatalogResult<PluginMediaContent>.Fail(CatalogFailures.MediaNotFound());
        }

        if (media.StorageKey is null)
        {
            return CatalogResult<PluginMediaContent>.Fail(CatalogFailures.ExternalMediaHasNoContent());
        }

        if (string.IsNullOrWhiteSpace(media.ContentType))
        {
            return CatalogResult<PluginMediaContent>.Fail(CatalogFailures.MediaContentUnavailable());
        }

        try
        {
            var content = await storage.OpenReadAsync(
                media.StorageKey,
                variant,
                media.ContentType,
                cancellationToken);

            return content is null
                ? CatalogResult<PluginMediaContent>.Fail(CatalogFailures.MediaContentUnavailable())
                : CatalogResult<PluginMediaContent>.Success(content);
        }
        catch (PluginMediaStorageException)
        {
            return CatalogResult<PluginMediaContent>.Fail(CatalogFailures.MediaContentUnavailable());
        }
    }
}
