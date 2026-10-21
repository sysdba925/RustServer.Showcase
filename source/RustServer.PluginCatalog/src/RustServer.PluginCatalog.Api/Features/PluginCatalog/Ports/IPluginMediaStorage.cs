namespace RustServer.PluginCatalog.Api.Features.PluginCatalog.Ports;

public enum PluginMediaStorageFailureKind
{
    UploadTooLarge,
    UnsupportedMediaType,
    InvalidImage,
    Unavailable
}

public sealed class PluginMediaStorageException(PluginMediaStorageFailureKind kind, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    public PluginMediaStorageFailureKind Kind { get; } = kind;
}

public sealed record PluginMediaStorageWriteRequest(
    Guid PluginId,
    Guid MediaId,
    Stream Content);

public sealed record StoredPluginMedia(
    string StorageKey,
    string ContentType);

public enum PluginMediaStorageVariant
{
    Original,
    Thumbnail
}

public sealed record PluginMediaContent(
    Stream Stream,
    string ContentType);

public interface IPluginMediaStorage
{
    Task<StoredPluginMedia> SaveAsync(
        PluginMediaStorageWriteRequest request,
        CancellationToken cancellationToken);

    Task<PluginMediaContent?> OpenReadAsync(
        string storageKey,
        PluginMediaStorageVariant variant,
        string originalContentType,
        CancellationToken cancellationToken);

    Task DeleteAsync(string storageKey, CancellationToken cancellationToken);
}
