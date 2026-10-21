using RustServer.PluginCatalog.Api.Features.PluginCatalog.Media.Transport;
using RustServer.PluginCatalog.Api.Features.PluginCatalog.Organization.Transport;
using RustServer.PluginCatalog.Api.Features.PluginCatalog.Plugins.Domain;
using RustServer.PluginCatalog.Api.Features.PluginCatalog.Sources.Transport;

namespace RustServer.PluginCatalog.Api.Features.PluginCatalog.Plugins.Transport;

public sealed record PluginSourceInput
{
    public required string Url { get; init; }
    public Guid? SiteId { get; init; }
    public string? Label { get; init; }
}

public sealed record CreatePluginRequest
{
    public required string Name { get; init; }

    public string? Description { get; init; }
    public string? Author { get; init; }
    public string? Notes { get; init; }
    public string? SourceDescription { get; init; }
    public decimal? Price { get; init; }
    public string? Currency { get; init; }

    public string? CurrentKnownVersion { get; init; }

    public DateOnly? LastPublishedDate { get; init; }
    public List<PluginSourceInput>? Sources { get; init; }
    public List<Guid>? TagIds { get; init; }
    public List<Guid>? FolderIds { get; init; }
}

public sealed record UpdatePluginRequest
{
    public required string Name { get; init; }

    public string? Description { get; init; }
    public string? Author { get; init; }
    public string? Notes { get; init; }
    public string? SourceDescription { get; init; }
    public decimal? Price { get; init; }
    public string? Currency { get; init; }

    public string? CurrentKnownVersion { get; init; }

    public DateOnly? LastPublishedDate { get; init; }
    public List<PluginSourceInput>? Sources { get; init; }
    public List<Guid>? TagIds { get; init; }
    public List<Guid>? FolderIds { get; init; }
}

public sealed record PluginSummaryDto(
    Guid Id,
    string Name,
    string Slug,
    string ShortDescription,
    string? Description,
    string? Author,
    string? Notes,
    string? SourceDescription,
    decimal? Price,
    string? Currency,
    string? CurrentKnownVersion,
    DateOnly? LastPublishedDate,
    DateTimeOffset UpdatedAt,
    PluginIconDto? Icon,
    IReadOnlyList<PluginSourceDto> Sources,
    IReadOnlyList<TagDto> Tags,
    IReadOnlyList<FolderDto> Folders);

public sealed record PluginDetailsDto(
    Guid Id,
    string Name,
    string Slug,
    string ShortDescription,
    string? Description,
    string? Author,
    string? Notes,
    string? SourceDescription,
    decimal? Price,
    string? Currency,
    string? CurrentKnownVersion,
    DateOnly? LastPublishedDate,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<PluginSourceDto> Sources,
    Guid? IconMediaId,
    IReadOnlyList<PluginMediaDto> Media,
    PluginIconDto? Icon,
    IReadOnlyList<TagDto> Tags,
    IReadOnlyList<FolderDto> Folders);

public sealed record PagedResultDto<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    int TotalItems,
    int TotalPages);

public static class PluginDtoMapper
{
    private const int ShortDescriptionMaxLength = 240;

    public static PluginSummaryDto ToSummary(this Plugin plugin) =>
        new(
            plugin.Id,
            plugin.Name,
            plugin.Slug,
            ToShortDescription(plugin.Description),
            plugin.Description,
            plugin.Author,
            plugin.Notes,
            plugin.SourceDescription,
            plugin.Price,
            plugin.Currency,
            plugin.CurrentKnownVersion,
            plugin.LastPublishedDate,
            plugin.UpdatedAt,
            Icon: plugin.IconMedia?.ToIconDto(),
            Sources: plugin.Sources.OrderBy(source => source.Id).Select(source => source.ToDto()).ToArray(),
            Tags: plugin.Tags.OrderBy(x => x.Name).ThenBy(x => x.Id).Select(x => new TagDto(x.Id, x.Name, x.Color)).ToArray(),
            Folders: plugin.Folders.OrderBy(x => x.Name).ThenBy(x => x.Id).Select(x => new FolderDto(x.Id, x.Name)).ToArray());

    public static PluginDetailsDto ToDetails(this Plugin plugin) =>
        new(
            plugin.Id,
            plugin.Name,
            plugin.Slug,
            ToShortDescription(plugin.Description),
            plugin.Description,
            plugin.Author,
            plugin.Notes,
            plugin.SourceDescription,
            plugin.Price,
            plugin.Currency,
            plugin.CurrentKnownVersion,
            plugin.LastPublishedDate,
            plugin.CreatedAt,
            plugin.UpdatedAt,
            plugin.Sources
                .OrderBy(source => source.Id)
                .Select(source => source.ToDto())
                .ToArray(),
            IconMediaId: plugin.IconMediaId,
            Media: plugin.Media
                .OrderBy(media => media.SortOrder)
                .ThenBy(media => media.Id)
                .Select(media => media.ToDto())
                .ToArray(),
            Icon: plugin.IconMedia?.ToIconDto(),
            Tags: plugin.Tags.OrderBy(x => x.Name).ThenBy(x => x.Id).Select(x => new TagDto(x.Id, x.Name, x.Color)).ToArray(),
            Folders: plugin.Folders.OrderBy(x => x.Name).ThenBy(x => x.Id).Select(x => new FolderDto(x.Id, x.Name)).ToArray());

    private static string ToShortDescription(string? description) =>
        description is null ? "" : description.Length <= ShortDescriptionMaxLength
            ? description
            : string.Concat(description.AsSpan(0, ShortDescriptionMaxLength - 1), "…");
}
