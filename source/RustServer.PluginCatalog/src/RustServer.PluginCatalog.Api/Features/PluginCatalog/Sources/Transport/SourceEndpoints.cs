using Microsoft.AspNetCore.Mvc;
using RustServer.PluginCatalog.Api.Features.PluginCatalog.Sources.Commands;
using RustServer.PluginCatalog.Api.Features.PluginCatalog.Sources.Queries;
using RustServer.PluginCatalog.Api.Features.PluginCatalog.Transport;

namespace RustServer.PluginCatalog.Api.Features.PluginCatalog.Sources.Transport;

public static class SourceEndpoints
{
    public static RouteGroupBuilder MapSourceEndpoints(this RouteGroupBuilder plugins)
    {
        var group = plugins.MapGroup("/{pluginId:guid}/sources")
            .WithTags("Plugin sources");

        group.MapGet("", GetAllAsync)
            .WithName("ListPluginSources")
            .Produces<IReadOnlyList<PluginSourceDto>>()
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound);

        group.MapPost("", CreateAsync)
            .WithName("CreatePluginSource")
            .Accepts<CreateSourceRequest>("application/json")
            .Produces<PluginSourceDto>(StatusCodes.Status201Created)
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound);

        group.MapPut("/{sourceId:guid}", UpdateAsync)
            .WithName("UpdatePluginSource")
            .Accepts<UpdateSourceRequest>("application/json")
            .Produces<PluginSourceDto>()
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound);

        group.MapDelete("/{sourceId:guid}", DeleteAsync)
            .WithName("DeletePluginSource")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound);

        return plugins;
    }

    private static async Task<IResult> GetAllAsync(
        Guid pluginId,
        GetSourcesHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(pluginId, cancellationToken);
        return CatalogHttpResults.From(result, sources => Results.Ok(sources.Select(source => source.ToDto()).ToArray()));
    }

    private static async Task<IResult> CreateAsync(
        Guid pluginId,
        CreateSourceRequest request,
        CreateSourceHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(
            new CreateSourceCommand(pluginId, request.SiteId, request.Url, request.Label),
            cancellationToken);

        return CatalogHttpResults.From(result, source => Results.Created(
            $"/api/plugins/{pluginId}/sources",
            source.ToDto()));
    }

    private static async Task<IResult> UpdateAsync(
        Guid pluginId,
        Guid sourceId,
        UpdateSourceRequest request,
        UpdateSourceHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(
            new UpdateSourceCommand(pluginId, sourceId, request.SiteId, request.Url, request.Label),
            cancellationToken);

        return CatalogHttpResults.From(result, source => Results.Ok(source.ToDto()));
    }

    private static async Task<IResult> DeleteAsync(
        Guid pluginId,
        Guid sourceId,
        DeleteSourceHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(pluginId, sourceId, cancellationToken);
        return CatalogHttpResults.From(result, _ => Results.NoContent());
    }
}
