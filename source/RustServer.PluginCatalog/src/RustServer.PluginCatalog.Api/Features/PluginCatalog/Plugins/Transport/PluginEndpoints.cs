using Microsoft.AspNetCore.Mvc;
using RustServer.PluginCatalog.Api.Features.PluginCatalog.Plugins.Commands;
using RustServer.PluginCatalog.Api.Features.PluginCatalog.Plugins.Queries;
using RustServer.PluginCatalog.Api.Features.PluginCatalog.Sources.Transport;
using RustServer.PluginCatalog.Api.Features.PluginCatalog.Transport;
using System.Globalization;

namespace RustServer.PluginCatalog.Api.Features.PluginCatalog.Plugins.Transport;

public static class PluginEndpoints
{
    public static RouteGroupBuilder MapPluginEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/plugins")
            .WithTags("Plugins");

        group.MapGet("", ListAsync)
            .WithName("ListPlugins")
            .Produces<PagedResultDto<PluginSummaryDto>>()
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest);

        group.MapPost("", CreateAsync)
            .WithName("CreatePlugin")
            .Accepts<CreatePluginRequest>("application/json")
            .Produces<PluginDetailsDto>(StatusCodes.Status201Created)
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
            .Produces<ProblemDetails>(StatusCodes.Status409Conflict);

        group.MapGet("/{pluginId:guid}", GetByIdAsync)
            .WithName("GetPluginById")
            .Produces<PluginDetailsDto>()
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound);

        group.MapPut("/{pluginId:guid}", UpdateAsync)
            .WithName("UpdatePlugin")
            .Accepts<UpdatePluginRequest>("application/json")
            .Produces<PluginDetailsDto>()
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
            .Produces<ProblemDetails>(StatusCodes.Status409Conflict);

        group.MapDelete("/{pluginId:guid}", DeleteAsync)
            .WithName("DeletePlugin")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound);

        group.MapSourceEndpoints();

        return group;
    }

    private static async Task<IResult> ListAsync(
        string? page,
        string? pageSize,
        string? search,
        Guid? siteId,
        Guid? tagId,
        Guid? folderId,
        ListPluginsHandler handler,
        CancellationToken cancellationToken)
    {
        if (!TryParseInteger(page, ListPluginsHandler.DefaultPage, out var parsedPage)
            || !TryParseInteger(pageSize, ListPluginsHandler.DefaultPageSize, out var parsedPageSize))
        {
            return CatalogHttpResults.Problem(CatalogFailures.Validation("page and pageSize must be integers."));
        }

        var result = await handler.HandleAsync(
            new ListPluginsQuery(parsedPage, parsedPageSize, search, siteId, tagId, folderId),
            cancellationToken);

        return CatalogHttpResults.From(result, plugins => Results.Ok(
            new PagedResultDto<PluginSummaryDto>(
                plugins.Items.Select(plugin => plugin.ToSummary()).ToArray(),
                plugins.Page,
                plugins.PageSize,
                plugins.TotalItems,
                plugins.TotalItems == 0
                    ? 0
                    : (int) Math.Ceiling((double) plugins.TotalItems / plugins.PageSize))));
    }

    private static async Task<IResult> CreateAsync(
        CreatePluginRequest request,
        CreatePluginHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(
            new CreatePluginCommand(
                request.Name,
                request.Description,
                request.Author,
                request.Notes,
                request.SourceDescription,
                request.Price,
                request.Currency,
                request.CurrentKnownVersion,
                request.LastPublishedDate,
                request.Sources,
                request.TagIds,
                request.FolderIds),
            cancellationToken);

        return CatalogHttpResults.From(result, plugin => Results.CreatedAtRoute(
            "GetPluginById",
            new { pluginId = plugin.Id },
            plugin.ToDetails()));
    }

    private static async Task<IResult> GetByIdAsync(
        Guid pluginId,
        GetPluginDetailsHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(pluginId, cancellationToken);
        return CatalogHttpResults.From(result, plugin => Results.Ok(plugin.ToDetails()));
    }

    private static async Task<IResult> UpdateAsync(
        Guid pluginId,
        UpdatePluginRequest request,
        UpdatePluginHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(
            new UpdatePluginCommand(
                pluginId,
                request.Name,
                request.Description,
                request.Author,
                request.Notes,
                request.SourceDescription,
                request.Price,
                request.Currency,
                request.CurrentKnownVersion,
                request.LastPublishedDate,
                request.Sources,
                request.TagIds,
                request.FolderIds),
            cancellationToken);

        return CatalogHttpResults.From(result, plugin => Results.Ok(plugin.ToDetails()));
    }

    private static async Task<IResult> DeleteAsync(
        Guid pluginId,
        DeletePluginHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(pluginId, cancellationToken);
        return CatalogHttpResults.From(result, _ => Results.NoContent());
    }

    private static bool TryParseInteger(string? value, int defaultValue, out int parsed)
    {
        if (value is null)
        {
            parsed = defaultValue;
            return true;
        }

        return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out parsed);
    }
}
