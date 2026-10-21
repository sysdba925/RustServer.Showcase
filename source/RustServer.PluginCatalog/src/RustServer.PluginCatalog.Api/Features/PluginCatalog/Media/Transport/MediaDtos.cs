using RustServer.PluginCatalog.Api.Features.PluginCatalog.Plugins.Domain;

namespace RustServer.PluginCatalog.Api.Features.PluginCatalog.Media.Transport;

public sealed record PluginMediaDto(
    Guid Id,
    PluginMediaKind Kind,
    string? Caption,
    int SortOrder,
    string? ContentType,
    string AccessUrl,
    string? ThumbnailAccessUrl);

public sealed record PluginIconDto(
    Guid Id,
    string AccessUrl,
    string ThumbnailAccessUrl,
    string ContentType);

public sealed record UpdatePluginMediaRequest
{
    public string? Caption { get; init; }

    public int? SortOrder { get; init; }
}

public sealed record UploadPluginMediaRequest
{
    public required IFormFile File { get; init; }

    public required string Kind { get; init; }

    public string? Caption { get; init; }

    public required string SortOrder { get; init; }
}

public sealed record CreateExternalPluginMediaRequest
{
    public required PluginMediaKind Kind { get; init; }

    public required string ExternalUrl { get; init; }

    public string? Caption { get; init; }

    public int? SortOrder { get; init; }
}

public sealed record SetPluginIconRequest
{
    public Guid? MediaId { get; init; }
}

public static class PluginMediaDtoMapper
{
    public static PluginMediaDto ToDto(this PluginMedia media) =>
        new(
            media.Id,
            media.Kind,
            media.Caption,
            media.SortOrder,
            media.ContentType,
            GetAccessUrl(media),
            media.StorageKey is null
                ? null
                : MediaAccessUrls.Thumbnail(media.PluginId, media.Id));

    public static PluginIconDto ToIconDto(this PluginMedia media)
    {
        if (media.Kind != PluginMediaKind.Icon
            || media.StorageKey is null
            || string.IsNullOrWhiteSpace(media.ContentType))
        {
            throw new InvalidOperationException("Selected icon must be local Icon media with a content type.");
        }

        return new PluginIconDto(
            media.Id,
            MediaAccessUrls.Original(media.PluginId, media.Id),
            MediaAccessUrls.Thumbnail(media.PluginId, media.Id),
            media.ContentType);
    }

    private static string GetAccessUrl(PluginMedia media) =>
        media.StorageKey is null
            ? media.ExternalUrl ?? throw new InvalidOperationException("External media must have an external URL.")
            : MediaAccessUrls.Original(media.PluginId, media.Id);
}

public static class MediaAccessUrls
{
    public static string Original(Guid pluginId, Guid mediaId) =>
        $"/api/plugins/{pluginId:D}/media/{mediaId:D}/content";

    public static string Thumbnail(Guid pluginId, Guid mediaId) =>
        $"{Original(pluginId, mediaId)}?variant=thumbnail";
}
