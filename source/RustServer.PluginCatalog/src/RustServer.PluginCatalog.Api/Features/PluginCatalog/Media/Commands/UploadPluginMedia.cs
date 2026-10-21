using RustServer.PluginCatalog.Api.Features.PluginCatalog.Plugins.Domain;
using RustServer.PluginCatalog.Api.Features.PluginCatalog.Ports;

namespace RustServer.PluginCatalog.Api.Features.PluginCatalog.Media.Commands;

public sealed record UploadPluginMediaCommand(
    Guid PluginId,
    PluginMediaKind Kind,
    string? Caption,
    int SortOrder,
    Stream Content);

public sealed class UploadPluginMediaHandler(
    IPluginCatalogStore store,
    IPluginMediaStorage storage,
    ILogger<UploadPluginMediaHandler> logger)
{
    public async Task<CatalogResult<PluginMedia>> HandleAsync(
        UploadPluginMediaCommand command,
        CancellationToken cancellationToken)
    {
        if (!IsLocalUploadKind(command.Kind))
        {
            return CatalogResult<PluginMedia>.Fail(
                CatalogFailures.Validation("kind must be Icon, Screenshot, AnimatedImage, Preview or Technical for a local upload."));
        }

        try
        {
            PluginMedia.ValidateMetadata(command.Caption, command.SortOrder);
        }
        catch (ArgumentException exception)
        {
            return CatalogResult<PluginMedia>.Fail(CatalogFailures.Validation(exception.Message));
        }

        if (!await store.PluginExistsAsync(command.PluginId, cancellationToken))
        {
            return CatalogResult<PluginMedia>.Fail(CatalogFailures.PluginNotFound());
        }

        var mediaId = Guid.NewGuid();
        StoredPluginMedia stored;
        try
        {
            stored = await storage.SaveAsync(
                new PluginMediaStorageWriteRequest(command.PluginId, mediaId, command.Content),
                cancellationToken);
        }
        catch (PluginMediaStorageException exception)
        {
            return CatalogResult<PluginMedia>.Fail(ToCatalogFailure(exception.Kind));
        }

        PluginMedia media;
        try
        {
            media = new PluginMedia(
                mediaId,
                command.PluginId,
                command.Kind,
                command.Caption,
                command.SortOrder,
                stored.StorageKey,
                null,
                stored.ContentType);
        }
        catch (ArgumentException)
        {
            await TryCleanupAsync(stored.StorageKey, mediaId);
            throw;
        }

        try
        {
            store.AddMedia(media);
            await store.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            await TryCleanupAsync(stored.StorageKey, mediaId);
            throw;
        }

        return CatalogResult<PluginMedia>.Success(media);
    }

    private async Task TryCleanupAsync(string storageKey, Guid mediaId)
    {
        try
        {
            await storage.DeleteAsync(storageKey, CancellationToken.None);
        }
        catch (Exception exception) when (exception is PluginMediaStorageException or IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(exception, "Could not clean up media storage after persistence failure for media {MediaId}.", mediaId);
        }
    }

    private static bool IsLocalUploadKind(PluginMediaKind kind) =>
        kind is PluginMediaKind.Icon
            or PluginMediaKind.Screenshot
            or PluginMediaKind.AnimatedImage
            or PluginMediaKind.Preview
            or PluginMediaKind.Technical;

    private static CatalogFailure ToCatalogFailure(PluginMediaStorageFailureKind kind) => kind switch
    {
        PluginMediaStorageFailureKind.UploadTooLarge => CatalogFailures.UploadTooLarge(),
        PluginMediaStorageFailureKind.UnsupportedMediaType => CatalogFailures.UnsupportedMediaType(),
        PluginMediaStorageFailureKind.InvalidImage => CatalogFailures.Validation("The uploaded file is not a valid supported image."),
        PluginMediaStorageFailureKind.Unavailable => CatalogFailures.MediaStorageUnavailable(),
        _ => throw new InvalidOperationException($"Unsupported media storage failure: {kind}.")
    };
}
