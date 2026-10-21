using Microsoft.AspNetCore.Mvc;
using RustServer.PluginCatalog.Api.Features.PluginCatalog.Media.Commands;
using RustServer.PluginCatalog.Api.Features.PluginCatalog.Media.Queries;
using RustServer.PluginCatalog.Api.Features.PluginCatalog.Plugins.Domain;
using RustServer.PluginCatalog.Api.Features.PluginCatalog.Plugins.Transport;
using RustServer.PluginCatalog.Api.Features.PluginCatalog.Ports;
using RustServer.PluginCatalog.Api.Features.PluginCatalog.Transport;
using System.Globalization;

namespace RustServer.PluginCatalog.Api.Features.PluginCatalog.Media.Transport;

public static class MediaEndpoints
{
    public static IEndpointRouteBuilder MapPluginMediaEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/plugins/{pluginId:guid}/media")
            .WithTags("Plugin media");

        group.MapGet("", ListAsync)
            .WithName("ListPluginMedia")
            .Produces<IReadOnlyList<PluginMediaDto>>()
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound);

        group.MapPost("", UploadAsync)
            .WithName("UploadPluginMedia")
            .Accepts<UploadPluginMediaRequest>("multipart/form-data")
            .DisableAntiforgery()
            .Produces<PluginMediaDto>(StatusCodes.Status201Created)
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
            .Produces<ProblemDetails>(StatusCodes.Status413PayloadTooLarge)
            .Produces<ProblemDetails>(StatusCodes.Status415UnsupportedMediaType)
            .Produces<ProblemDetails>(StatusCodes.Status503ServiceUnavailable);

        group.MapPost("/external", CreateExternalAsync)
            .WithName("CreateExternalPluginMedia")
            .Accepts<CreateExternalPluginMediaRequest>("application/json")
            .Produces<PluginMediaDto>(StatusCodes.Status201Created)
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound);

        group.MapPut("/{mediaId:guid}", UpdateAsync)
            .WithName("UpdatePluginMedia")
            .Accepts<UpdatePluginMediaRequest>("application/json")
            .Produces<PluginMediaDto>()
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound);

        group.MapDelete("/{mediaId:guid}", DeleteAsync)
            .WithName("DeletePluginMedia")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound);

        group.MapGet("/{mediaId:guid}/content", GetContentAsync)
            .WithName("GetPluginMediaContent")
            .Produces(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
            .Produces<ProblemDetails>(StatusCodes.Status409Conflict)
            .Produces<ProblemDetails>(StatusCodes.Status503ServiceUnavailable);

        endpoints.MapPut("/api/plugins/{pluginId:guid}/icon", SetIconAsync)
            .WithTags("Plugin media")
            .WithName("SetPluginIcon")
            .Accepts<SetPluginIconRequest>("application/json")
            .Produces<PluginDetailsDto>()
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
            .Produces<ProblemDetails>(StatusCodes.Status409Conflict);

        return endpoints;
    }

    private static async Task<IResult> ListAsync(
        Guid pluginId,
        GetPluginMediaHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(pluginId, cancellationToken);
        return CatalogHttpResults.From(result, media => Results.Ok(media.Select(item => item.ToDto()).ToArray()));
    }

    private static async Task<IResult> UploadAsync(
        Guid pluginId,
        [FromForm] UploadPluginMediaRequest request,
        UploadPluginMediaHandler handler,
        CancellationToken cancellationToken)
    {
        if (request.File is null || request.File.Length == 0)
        {
            return CatalogHttpResults.Problem(CatalogFailures.Validation("file must contain image bytes."));
        }

        if (!TryParseMediaKind(request.Kind, out var parsedKind))
        {
            return CatalogHttpResults.Problem(CatalogFailures.Validation("kind is not supported."));
        }

        if (!int.TryParse(request.SortOrder, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedSortOrder))
        {
            return CatalogHttpResults.Problem(CatalogFailures.Validation("sortOrder must be a non-negative integer."));
        }

        await using var content = request.File.OpenReadStream();
        var result = await handler.HandleAsync(
            new UploadPluginMediaCommand(pluginId, parsedKind, request.Caption, parsedSortOrder, content),
            cancellationToken);

        return CatalogHttpResults.From(result, media => Results.Created(
            $"/api/plugins/{pluginId:D}/media/{media.Id:D}",
            media.ToDto()));
    }

    private static async Task<IResult> CreateExternalAsync(
        Guid pluginId,
        CreateExternalPluginMediaRequest request,
        CreateExternalPluginMediaHandler handler,
        CancellationToken cancellationToken)
    {
        if (request.SortOrder is null)
        {
            return CatalogHttpResults.Problem(CatalogFailures.Validation("sortOrder is required."));
        }

        var result = await handler.HandleAsync(
            new CreateExternalPluginMediaCommand(
                pluginId,
                request.Kind,
                request.ExternalUrl,
                request.Caption,
                request.SortOrder.Value),
            cancellationToken);

        return CatalogHttpResults.From(result, media => Results.Created(
            $"/api/plugins/{pluginId:D}/media/{media.Id:D}",
            media.ToDto()));
    }

    private static async Task<IResult> UpdateAsync(
        Guid pluginId,
        Guid mediaId,
        UpdatePluginMediaRequest request,
        UpdatePluginMediaHandler handler,
        CancellationToken cancellationToken)
    {
        if (request.SortOrder is null)
        {
            return CatalogHttpResults.Problem(CatalogFailures.Validation("sortOrder is required."));
        }

        var result = await handler.HandleAsync(
            new UpdatePluginMediaCommand(pluginId, mediaId, request.Caption, request.SortOrder.Value),
            cancellationToken);

        return CatalogHttpResults.From(result, media => Results.Ok(media.ToDto()));
    }

    private static async Task<IResult> DeleteAsync(
        Guid pluginId,
        Guid mediaId,
        DeletePluginMediaHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(pluginId, mediaId, cancellationToken);
        return CatalogHttpResults.From(result, _ => Results.NoContent());
    }

    private static async Task<IResult> SetIconAsync(
        Guid pluginId,
        SetPluginIconRequest request,
        SetPluginIconHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new SetPluginIconCommand(pluginId, request.MediaId), cancellationToken);
        return CatalogHttpResults.From(result, plugin => Results.Ok(plugin.ToDetails()));
    }

    private static async Task<IResult> GetContentAsync(
        Guid pluginId,
        Guid mediaId,
        string? variant,
        HttpContext httpContext,
        GetPluginMediaContentHandler handler,
        CancellationToken cancellationToken)
    {
        if (!TryParseVariant(variant, out var parsedVariant))
        {
            return CatalogHttpResults.Problem(CatalogFailures.Validation("variant must be thumbnail when supplied."));
        }

        var result = await handler.HandleAsync(pluginId, mediaId, parsedVariant, cancellationToken);
        return CatalogHttpResults.From(result, content =>
        {
            httpContext.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
            return Results.Stream(content.Stream, content.ContentType, enableRangeProcessing: true);
        });
    }

    private static bool TryParseMediaKind(string? value, out PluginMediaKind kind) =>
        Enum.TryParse(value, ignoreCase: false, out kind) && Enum.IsDefined(kind);

    private static bool TryParseVariant(string? value, out PluginMediaStorageVariant variant)
    {
        if (value is null)
        {
            variant = PluginMediaStorageVariant.Original;
            return true;
        }

        if (string.Equals(value, "thumbnail", StringComparison.Ordinal))
        {
            variant = PluginMediaStorageVariant.Thumbnail;
            return true;
        }

        variant = default;
        return false;
    }
}
