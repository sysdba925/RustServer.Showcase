namespace RustServer.PluginCatalog.Api.Features.PluginCatalog.Plugins.Domain;

public sealed class PluginSource
{
    public const int UrlMaxLength = 2048;
    public const int LabelMaxLength = 200;

    private PluginSource()
    {
        Url = null!;
    }

    public PluginSource(
        Guid id,
        Guid pluginId,
        Guid? siteId,
        string url,
        string? label)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Source ID must not be empty.", nameof(id));
        }

        if (pluginId == Guid.Empty)
        {
            throw new ArgumentException("Plugin ID must not be empty.", nameof(pluginId));
        }

        Id = id;
        PluginId = pluginId;
        SiteId = siteId;
        Url = NormalizeHttpUrl(url, nameof(url));
        Label = NormalizeOptionalText(label, nameof(label), LabelMaxLength);
    }

    public Guid Id { get; private set; }

    public Guid PluginId { get; private set; }

    public Guid? SiteId { get; private set; }

    public string Url { get; private set; }

    public string? Label { get; private set; }

    public Plugin? Plugin { get; private set; }

    public void Update(Guid? siteId, string url, string? label)
    {
        var normalizedUrl = NormalizeHttpUrl(url, nameof(url));
        var normalizedLabel = NormalizeOptionalText(label, nameof(label), LabelMaxLength);
        SiteId = siteId;
        Url = normalizedUrl;
        Label = normalizedLabel;
    }

    private static string NormalizeHttpUrl(string value, string parameterName)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized)
            || !Uri.TryCreate(normalized, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException("Value must be an absolute HTTP or HTTPS URL.", parameterName);
        }

        var absoluteUrl = uri.AbsoluteUri;
        if (absoluteUrl.Length > UrlMaxLength)
        {
            throw new ArgumentException($"Value must not exceed {UrlMaxLength} characters.", parameterName);
        }

        return absoluteUrl;
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
