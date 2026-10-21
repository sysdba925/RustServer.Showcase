using Microsoft.AspNetCore.Mvc;
using RustServer.PluginCatalog.Api.Features.PluginCatalog.Sites.Commands;
using RustServer.PluginCatalog.Api.Features.PluginCatalog.Sites.Domain;
using RustServer.PluginCatalog.Api.Features.PluginCatalog.Transport;

namespace RustServer.PluginCatalog.Api.Features.PluginCatalog.Sites.Transport;

public sealed record SiteRequest(string? Name, string? BaseUrl);
public sealed record SiteDto(Guid Id, string Name, string? BaseUrl);

public static class SiteEndpoints
{
    public static void MapSiteEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/sites").WithTags("Sites");
        group.MapGet("", async (SiteHandler h, CancellationToken ct) => Results.Ok((await h.ListAsync(ct)).Select(ToDto))).Produces<SiteDto[]>();
        group.MapGet("/{id:guid}", async (Guid id, SiteHandler h, CancellationToken ct) => (await h.FindAsync(id, ct)) is { } site ? Results.Ok(ToDto(site)) : CatalogHttpResults.Problem(SiteHandler.NotFound())).Produces<SiteDto>().Produces<ProblemDetails>(404);
        group.MapPost("", async (SiteRequest request, SiteHandler h, CancellationToken ct) => CatalogHttpResults.From(await h.SaveAsync(null, request.Name, request.BaseUrl, ct), site => Results.Created($"/api/sites/{site.Id}", ToDto(site)))).Produces<SiteDto>(201).Produces<ProblemDetails>(400);
        group.MapPut("/{id:guid}", async (Guid id, SiteRequest request, SiteHandler h, CancellationToken ct) => CatalogHttpResults.From(await h.SaveAsync(id, request.Name, request.BaseUrl, ct), site => Results.Ok(ToDto(site)))).Produces<SiteDto>().Produces<ProblemDetails>(400).Produces<ProblemDetails>(404);
        group.MapDelete("/{id:guid}", async (Guid id, SiteHandler h, CancellationToken ct) => CatalogHttpResults.From(await h.DeleteAsync(id, ct), _ => Results.NoContent())).Produces(204).Produces<ProblemDetails>(404);
    }
    private static SiteDto ToDto(Site site)
    {
        return new(site.Id, site.Name, site.BaseUrl);
    }
}
