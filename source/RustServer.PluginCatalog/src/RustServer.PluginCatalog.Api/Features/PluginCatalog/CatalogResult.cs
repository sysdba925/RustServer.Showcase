namespace RustServer.PluginCatalog.Api.Features.PluginCatalog;

public enum CatalogFailureKind
{
    Validation,
    NotFound,
    Conflict,
    PayloadTooLarge,
    UnsupportedMediaType,
    Unavailable
}

public sealed record CatalogFailure(
    CatalogFailureKind Kind,
    string Code,
    string Title,
    string Detail);

public sealed class CatalogResult<T>
{
    private CatalogResult(T? value, CatalogFailure? failure)
    {
        Value = value;
        Failure = failure;
    }

    public T? Value { get; }

    public CatalogFailure? Failure { get; }

    public bool IsSuccess => Failure is null;

    public static CatalogResult<T> Success(T value) => new(value, null);

    public static CatalogResult<T> Fail(CatalogFailure failure) => new(default, failure);
}

public static class CatalogFailures
{
    public static CatalogFailure Validation(string detail) =>
        new(CatalogFailureKind.Validation, "validation_failed", "The request is invalid.", detail);

    public static CatalogFailure PluginNotFound() =>
        new(CatalogFailureKind.NotFound, "plugin_not_found", "Plugin was not found.", "No catalog plugin has the requested ID.");

    public static CatalogFailure SourceNotFound() =>
        new(CatalogFailureKind.NotFound, "source_not_found", "Plugin source was not found.", "No source belonging to the requested plugin has the requested ID.");

    public static CatalogFailure SlugConflict() =>
        new(CatalogFailureKind.Conflict, "slug_conflict", "Slug is already in use.", "Another catalog plugin already uses this slug.");

    public static CatalogFailure MediaNotFound() =>
        new(CatalogFailureKind.NotFound, "media_not_found", "Plugin media was not found.", "No media belonging to the requested plugin has the requested ID.");

    public static CatalogFailure MediaIsNotIcon() =>
        new(CatalogFailureKind.Conflict, "media_is_not_icon", "Media cannot be selected as an icon.", "Only media with kind Icon can be selected as the plugin icon.");

    public static CatalogFailure ExternalMediaHasNoContent() =>
        new(CatalogFailureKind.Conflict, "external_media_has_no_content", "External media has no local content.", "Open the media accessUrl directly instead of requesting catalog content.");

    public static CatalogFailure UploadTooLarge() =>
        new(CatalogFailureKind.PayloadTooLarge, "upload_too_large", "The upload is too large.", "The uploaded file exceeds the configured media size limit.");

    public static CatalogFailure UnsupportedMediaType() =>
        new(CatalogFailureKind.UnsupportedMediaType, "unsupported_media_type", "The media type is not supported.", "Only PNG, JPEG, WebP and GIF image uploads are supported.");

    public static CatalogFailure MediaStorageUnavailable() =>
        new(CatalogFailureKind.Unavailable, "media_storage_unavailable", "Media storage is unavailable.", "The server could not store the uploaded media.");

    public static CatalogFailure MediaContentUnavailable() =>
        new(CatalogFailureKind.Unavailable, "media_content_unavailable", "Media content is unavailable.", "The server could not read the stored media content.");
}
