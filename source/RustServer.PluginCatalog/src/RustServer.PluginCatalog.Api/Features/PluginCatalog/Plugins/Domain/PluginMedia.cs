namespace RustServer.PluginCatalog.Api.Features.PluginCatalog.Plugins.Domain;

public sealed class PluginMedia
{
    public const int CaptionMaxLength = 500;
    public const int StorageKeyMaxLength = 1024;
    public const int ExternalUrlMaxLength = 2048;
    public const int ContentTypeMaxLength = 255;

    private PluginMedia()
    {
    }

    public PluginMedia(
        Guid id,
        Guid pluginId,
        PluginMediaKind kind,
        string? caption,
        int sortOrder,
        string? storageKey,
        string? externalUrl,
        string? contentType)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Media ID must not be empty.", nameof(id));
        }

        if (pluginId == Guid.Empty)
        {
            throw new ArgumentException("Plugin ID must not be empty.", nameof(pluginId));
        }

        ValidateMetadata(caption, sortOrder);

        Id = id;
        PluginId = pluginId;
        Kind = kind;
        Caption = NormalizeOptionalText(caption, nameof(caption), CaptionMaxLength);
        SortOrder = sortOrder;
        StorageKey = NormalizeStorageKey(storageKey);
        ExternalUrl = NormalizeExternalUrl(externalUrl);
        ContentType = NormalizeOptionalText(contentType, nameof(contentType), ContentTypeMaxLength);

        if ((StorageKey is null) == (ExternalUrl is null))
        {
            throw new ArgumentException("Exactly one of storage key or external URL must be set.");
        }
    }

    public Guid Id { get; private set; }

    public Guid PluginId { get; private set; }

    public PluginMediaKind Kind { get; private set; }

    public string? Caption { get; private set; }

    public int SortOrder { get; private set; }

    public string? StorageKey { get; private set; }

    public string? ExternalUrl { get; private set; }

    public string? ContentType { get; private set; }

    public Plugin? Plugin { get; private set; }

    public static void ValidateMetadata(string? caption, int sortOrder)
    {
        if (sortOrder < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sortOrder), "Sort order must not be negative.");
        }

        _ = NormalizeOptionalText(caption, nameof(caption), CaptionMaxLength);
    }

    public void UpdateMetadata(string? caption, int sortOrder)
    {
        ValidateMetadata(caption, sortOrder);

        Caption = NormalizeOptionalText(caption, nameof(caption), CaptionMaxLength);
        SortOrder = sortOrder;
    }

    private static string? NormalizeStorageKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        var segments = normalized.Split('/', StringSplitOptions.None);
        if (normalized.StartsWith('/')
            || normalized.StartsWith('\\')
            || normalized.Contains('\\')
            || normalized.Contains(':')
            || segments.Length == 0
            || segments.Any(segment => string.IsNullOrWhiteSpace(segment) || segment is "." or ".."))
        {
            throw new ArgumentException("Storage key must be a portable relative path using forward slashes.", nameof(value));
        }

        if (normalized.Length > StorageKeyMaxLength)
        {
            throw new ArgumentException($"Storage key must not exceed {StorageKeyMaxLength} characters.", nameof(value));
        }

        return normalized;
    }

    private static string? NormalizeExternalUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        if (normalized.Length > ExternalUrlMaxLength)
        {
            throw new ArgumentException($"External URL must not exceed {ExternalUrlMaxLength} characters.", nameof(value));
        }

        if (!Uri.TryCreate(normalized, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException("External URL must be an absolute HTTP or HTTPS URL.", nameof(value));
        }

        return uri.AbsoluteUri;
    }

    private static string? NormalizeOptionalText(string? value, string parameterName, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        if (normalized.Length > maxLength)
        {
            throw new ArgumentException($"Value must not exceed {maxLength} characters.", parameterName);
        }

        return normalized;
    }
}
