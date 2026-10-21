using RustServer.PluginCatalog.Api.Features.PluginCatalog.Organization.Domain;

namespace RustServer.PluginCatalog.Api.Features.PluginCatalog.Plugins.Domain;

public sealed class Plugin
{
    public const int NameMaxLength = 200;
    public const int SlugMaxLength = 160;
    public const int DescriptionMaxLength = 8000;
    public const int CurrentKnownVersionMaxLength = 100;

    private Plugin()
    {
        Name = null!;
        Slug = null!;
    }

    public Plugin(
        Guid id,
        string name,
        string slug,
        string? description,
        string? currentKnownVersion,
        DateOnly? lastPublishedDate,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Plugin ID must not be empty.", nameof(id));
        }

        Id = id;
        Name = RequireText(name, nameof(name), NameMaxLength);
        Slug = NormalizeSlug(slug);
        Description = NormalizeOptionalText(description, nameof(description), DescriptionMaxLength);
        CurrentKnownVersion = NormalizeOptionalText(currentKnownVersion, nameof(currentKnownVersion), CurrentKnownVersionMaxLength);
        LastPublishedDate = lastPublishedDate;
        CreatedAt = NormalizeUtc(createdAt);
        UpdatedAt = NormalizeUtc(updatedAt);

        if (UpdatedAt < CreatedAt)
        {
            throw new ArgumentException("Updated timestamp must not precede the creation timestamp.", nameof(updatedAt));
        }
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; }

    public string Slug { get; private set; }

    public string? Description { get; private set; }

    public string? Author { get; private set; }

    public string? Notes { get; private set; }

    public string? SourceDescription { get; private set; }

    public decimal? Price { get; private set; }

    public string? Currency { get; private set; }

    public string? CurrentKnownVersion { get; private set; }

    public DateOnly? LastPublishedDate { get; private set; }

    public Guid? IconMediaId { get; private set; }

    public PluginMedia? IconMedia { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public ICollection<Tag> Tags { get; } = new List<Tag>();

    public ICollection<Folder> Folders { get; } = new List<Folder>();

    public ICollection<PluginSource> Sources { get; } = new List<PluginSource>();

    public ICollection<PluginMedia> Media { get; } = new List<PluginMedia>();

    public void AddSource(PluginSource source)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (source.PluginId != Id)
        {
            throw new ArgumentException("Source must belong to this plugin.", nameof(source));
        }

        Sources.Add(source);
    }

    public void AddMedia(PluginMedia media)
    {
        ArgumentNullException.ThrowIfNull(media);

        if (media.PluginId != Id)
        {
            throw new ArgumentException("Media must belong to this plugin.", nameof(media));
        }

        Media.Add(media);
    }

    public void Update(
        string name,
        string slug,
        string? description,
        string? currentKnownVersion,
        DateOnly? lastPublishedDate,
        DateTimeOffset updatedAt)
    {
        var normalizedName = RequireText(name, nameof(name), NameMaxLength);
        var normalizedSlug = NormalizeSlug(slug);
        var normalizedDescription = NormalizeOptionalText(description, nameof(description), DescriptionMaxLength);
        var normalizedVersion = NormalizeOptionalText(currentKnownVersion, nameof(currentKnownVersion), CurrentKnownVersionMaxLength);
        var normalizedLastPublishedDate = lastPublishedDate;
        var normalizedUpdatedAt = NormalizeUtc(updatedAt);

        if (normalizedUpdatedAt < CreatedAt)
        {
            throw new ArgumentException("Updated timestamp must not precede the creation timestamp.", nameof(updatedAt));
        }

        Name = normalizedName;
        Slug = normalizedSlug;
        Description = normalizedDescription;
        CurrentKnownVersion = normalizedVersion;
        LastPublishedDate = normalizedLastPublishedDate;
        UpdatedAt = normalizedUpdatedAt;
    }

    public void SetLibraryDetails(string? author, string? notes, string? sourceDescription, decimal? price, string? currency)
    {
        var normalizedAuthor = NormalizeOptionalText(author, nameof(author), 200);
        var normalizedCurrency = string.IsNullOrWhiteSpace(currency) ? null : currency.Trim().ToUpperInvariant();
        if (price.HasValue != (normalizedCurrency is not null)
            || price is < 0 or > 9999999999999999.99m
            || (price.HasValue && decimal.Round(price.Value, 2) != price.Value)
            || (normalizedCurrency is not null && (normalizedCurrency.Length != 3
                || normalizedCurrency.Any(character => character is < 'A' or > 'Z'))))
        {
            throw new ArgumentException("Price and three-letter currency must be provided together; price must be non-negative with at most two decimal places.");
        }

        Author = normalizedAuthor;
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes;
        SourceDescription = string.IsNullOrWhiteSpace(sourceDescription) ? null : sourceDescription;
        Price = price;
        Currency = normalizedCurrency;
    }

    public void SetSlug(string slug) => Slug = NormalizeSlug(slug);

    public void SetIcon(PluginMedia? media)
    {
        if (media is not null)
        {
            if (media.PluginId != Id)
            {
                throw new ArgumentException("Icon media must belong to this plugin.", nameof(media));
            }

            if (media.Kind != PluginMediaKind.Icon)
            {
                throw new ArgumentException("Icon media must have Icon kind.", nameof(media));
            }
        }

        IconMedia = media;
        IconMediaId = media?.Id;
    }

    private static string NormalizeSlug(string value) =>
        RequireText(value, nameof(value), SlugMaxLength).ToLowerInvariant();

    private static string RequireText(string? value, string parameterName, int maxLength)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new ArgumentException("Value must not be empty.", parameterName);
        }

        if (normalized.Length > maxLength)
        {
            throw new ArgumentException($"Value must not exceed {maxLength} characters.", parameterName);
        }

        return normalized;
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

    private static DateTimeOffset NormalizeUtc(DateTimeOffset value) => value.ToUniversalTime();

    private static DateTimeOffset? NormalizeUtc(DateTimeOffset? value) =>
        value?.ToUniversalTime();
}
