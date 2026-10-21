using RustServer.PluginCatalog.Api.Features.PluginCatalog.Plugins.Domain;

namespace RustServer.PluginCatalog.Api.Features.PluginCatalog.Sources.Transport;

public sealed record CreateSourceRequest
{
    public Guid? SiteId { get; init; }

    public required string Url { get; init; }

    public string? Label { get; init; }
}

public sealed record UpdateSourceRequest
{
    public Guid? SiteId { get; init; }

    public required string Url { get; init; }

    public string? Label { get; init; }
}

public sealed record PluginSourceDto(
    Guid Id,
    Guid? SiteId,
    string Url,
    string? Label);

public static class PluginSourceDtoMapper
{
    public static PluginSourceDto ToDto(this PluginSource source) =>
        new(source.Id, source.SiteId, source.Url, source.Label);
}
