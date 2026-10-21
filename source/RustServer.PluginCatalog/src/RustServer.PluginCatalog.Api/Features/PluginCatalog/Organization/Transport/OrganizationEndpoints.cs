using Microsoft.AspNetCore.Mvc;
using RustServer.PluginCatalog.Api.Features.PluginCatalog.Organization.Commands;
using RustServer.PluginCatalog.Api.Features.PluginCatalog.Organization.Domain;
using RustServer.PluginCatalog.Api.Features.PluginCatalog.Transport;

namespace RustServer.PluginCatalog.Api.Features.PluginCatalog.Organization.Transport;

public sealed record TagDto(Guid Id, string Name, string Color);
public sealed record FolderDto(Guid Id, string Name);
public sealed record TagRequest(string? Name, string? Color);
public sealed record FolderRequest(string? Name);

public static class OrganizationEndpoints
{
    public static void MapOrganizationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var tags = endpoints.MapGroup("/api/tags")
            .WithTags("Tags");
        tags.MapGet("", async (OrganizationHandler h, CancellationToken ct) => Results.Ok((await h.ListTagsAsync(ct)).Select(ToDto)))
            .Produces<TagDto[]>();
        tags.MapGet("/{id:guid}", async (Guid id, OrganizationHandler h, CancellationToken ct) => (await h.GetTagAsync(id, ct)) is { } tag ? Results.Ok(ToDto(tag)) : CatalogHttpResults.Problem(OrganizationHandler.NotFound("tag")))
            .Produces<TagDto>()
            .Produces<ProblemDetails>(404);
        tags.MapPost("", async (TagRequest request, OrganizationHandler h, CancellationToken ct) => CatalogHttpResults.From(await h.SaveTagAsync(null, request.Name, request.Color, ct), tag => Results.Created($"/api/tags/{tag.Id}", ToDto(tag))))
            .Produces<TagDto>(201)
            .Produces<ProblemDetails>(400);
        tags.MapPut("/{id:guid}", async (Guid id, TagRequest request, OrganizationHandler h, CancellationToken ct) => CatalogHttpResults.From(await h.SaveTagAsync(id, request.Name, request.Color, ct), tag => Results.Ok(ToDto(tag))))
            .Produces<TagDto>()
            .Produces<ProblemDetails>(400)
            .Produces<ProblemDetails>(404);
        tags.MapDelete("/{id:guid}", async (Guid id, OrganizationHandler h, CancellationToken ct) => CatalogHttpResults.From(await h.DeleteAsync("tags", id, ct), _ => Results.NoContent()))
            .Produces(204)
            .Produces<ProblemDetails>(404);


        var folders = endpoints.MapGroup("/api/folders")
            .WithTags("Folders");
        folders.MapGet("", async (OrganizationHandler h, CancellationToken ct) => Results.Ok((await h.ListFoldersAsync(ct)).Select(ToDto)))
            .Produces<FolderDto[]>();
        folders.MapGet("/{id:guid}", async (Guid id, OrganizationHandler h, CancellationToken ct) => (await h.GetFolderAsync(id, ct)) is { } folder ? Results.Ok(ToDto(folder)) : CatalogHttpResults.Problem(OrganizationHandler.NotFound("folder")))
            .Produces<FolderDto>()
            .Produces<ProblemDetails>(404);
        folders.MapPost("", async (FolderRequest request, OrganizationHandler h, CancellationToken ct) => CatalogHttpResults.From(await h.SaveFolderAsync(null, request.Name, ct), folder => Results.Created($"/api/folders/{folder.Id}", ToDto(folder))))
            .Produces<FolderDto>(201)
            .Produces<ProblemDetails>(400);
        folders.MapPut("/{id:guid}", async (Guid id, FolderRequest request, OrganizationHandler h, CancellationToken ct) => CatalogHttpResults.From(await h.SaveFolderAsync(id, request.Name, ct), folder => Results.Ok(ToDto(folder))))
            .Produces<FolderDto>()
            .Produces<ProblemDetails>(400)
            .Produces<ProblemDetails>(404);
        folders.MapDelete("/{id:guid}", async (Guid id, OrganizationHandler h, CancellationToken ct) => CatalogHttpResults.From(await h.DeleteAsync("folders", id, ct), _ => Results.NoContent()))
            .Produces(204)
            .Produces<ProblemDetails>(404);

        MapAssignment(endpoints, "tags");
        MapAssignment(endpoints, "folders");
    }

    private static void MapAssignment(IEndpointRouteBuilder endpoints, string kind)
    {
        var route = $"/api/plugins/{{pluginId:guid}}/{kind}/{{id:guid}}";
        endpoints.MapPut(route, async (Guid pluginId, Guid id, OrganizationHandler h, CancellationToken ct) => CatalogHttpResults.From(await h.AssignAsync(pluginId, kind, id, true, ct), _ => Results.NoContent()))
            .WithTags("Plugins")
            .Produces(204)
            .Produces<ProblemDetails>(404);
        endpoints.MapDelete(route, async (Guid pluginId, Guid id, OrganizationHandler h, CancellationToken ct) => CatalogHttpResults.From(await h.AssignAsync(pluginId, kind, id, false, ct), _ => Results.NoContent()))
            .WithTags("Plugins")
            .Produces(204)
            .Produces<ProblemDetails>(404);
    }

    private static TagDto ToDto(Tag tag) => new(tag.Id, tag.Name, tag.Color);
    private static FolderDto ToDto(Folder folder) => new(folder.Id, folder.Name);
}
