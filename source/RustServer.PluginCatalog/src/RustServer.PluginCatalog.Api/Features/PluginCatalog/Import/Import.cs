using System.Text.Json.Serialization;

namespace RustServer.PluginCatalog.Api.Features.PluginCatalog.Import;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ImportRequest(int SchemaVersion, List<ImportPlugin?>? Plugins, bool CreateMissing = false);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ImportPlugin(string? Name, List<ImportSource?>? Sources, string? Author, string? Description, string? Notes, string? SourceDescription, ImportPrice? Price, List<string?>? Tags, List<string?>? Folders, string? CurrentKnownVersion = null, DateOnly? LastPublishedDate = null);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ImportSource(string? Url, string? Site, string? Label);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ImportPrice(decimal? Amount, string? Currency);
public sealed record ImportEntry(int Index, string? Name, ImportPlugin? Input, IReadOnlyList<string> Errors, IReadOnlyList<string> Duplicates, IReadOnlyList<string> UnknownSites, IReadOnlyList<string> UnknownTags, IReadOnlyList<string> UnknownFolders);
public sealed record ImportPreview(IReadOnlyList<string> Errors, IReadOnlyList<ImportEntry> Entries);
public sealed record ImportResult(ImportPreview Preview, int Created, int Skipped, bool Committed);
public interface IImportStore
{
    Task<ImportResult> ExecuteAsync(ImportRequest request, bool commit, CancellationToken ct);
}
public sealed class ImportHandler(IImportStore store)
{
    public Task<ImportResult> ExecuteAsync(ImportRequest request, bool commit, CancellationToken ct) => store.ExecuteAsync(request, commit, ct);
}
public static class ImportEndpoints
{
    public static void MapImportEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/import").WithTags("Import");
        group.MapPost("/preview", async (ImportRequest request, ImportHandler handler, CancellationToken ct) => Results.Ok((await handler.ExecuteAsync(request, false, ct)).Preview)).Produces<ImportPreview>();
        group.MapPost("/commit", async (ImportRequest request, ImportHandler handler, CancellationToken ct) =>
        {
            var result = await handler.ExecuteAsync(request, true, ct);
            return result.Committed ? Results.Ok(result) : Results.Json(result, statusCode: 400);
        }).Produces<ImportResult>().Produces<ImportResult>(400);
    }
}
