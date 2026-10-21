using Microsoft.AspNetCore.Mvc;
using RustServer.PluginCatalog.Api.Features.PluginCatalog.Export;
using System.IO.Compression;
using System.Text.Json;

namespace RustServer.PluginCatalog.Api.Features.PluginCatalog.Restore;

public sealed class RestoreConflictException : Exception;
public sealed class RestoreCommitUncertainException(Exception inner) : Exception("Database commit outcome is uncertain.", inner);
public interface IRestoreStore
{
    Task RestoreAsync(ExportCatalog catalog, Func<CancellationToken, Task> writeMedia, CancellationToken ct);
}
public interface IRestoreMediaStorage
{
    Task CreateOriginalAsync(string storageKey, Stream content, CancellationToken ct);
    Task DeleteCreatedOriginalAsync(string storageKey, CancellationToken ct);
}
public sealed record RestoreCommitResult(RestoreCounts Counts);

public sealed class RestoreCommitHandler(IRestoreStore store, IRestoreMediaStorage mediaStorage)
{
    public async Task<RestoreCommitResult> HandleAsync(Stream input, CancellationToken ct)
    {
        ExportCatalog catalog;
        byte[] archive;
        try
        {
            (catalog, archive) = await RestorePreviewHandler.ValidateArchiveAsync(input, ct);
        }
        catch (Exception e) when (e is JsonException or NotSupportedException or ArgumentException or IOException or InvalidOperationException)
        {
            throw new InvalidDataException("Invalid restore archive.", e);
        }

        using var buffer = new MemoryStream(archive, writable: false);
        using var zip = new ZipArchive(buffer, ZipArchiveMode.Read);
        var created = new List<string>();
        try
        {
            await store.RestoreAsync(catalog, async token =>
            {
                foreach (var media in catalog.Media.Where(m => m.StorageKey is not null))
                {
                    await using var source = zip.GetEntry(media.ArchivePath!)!.Open();
                    await mediaStorage.CreateOriginalAsync(media.StorageKey!, source, token);
                    created.Add(media.StorageKey!);
                }
            }, ct);
        }
        catch (RestoreCommitUncertainException) { throw; }
        catch (Exception original)
        {
            var failures = new List<Exception>();
            foreach (var key in created.AsEnumerable().Reverse())
            {
                try { await mediaStorage.DeleteCreatedOriginalAsync(key, CancellationToken.None); }
                catch (Exception e) { failures.Add(e); }
            }
            if (failures.Count > 0)
            {
                throw new AggregateException("Restore failed and cleanup was incomplete.", [original, .. failures]);
            }

            throw;
        }

        return new(RestorePreviewHandler.Counts(catalog));
    }
}

public static class RestoreCommitEndpoints
{
    public static void MapRestoreCommitEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/restore/commit", async (HttpRequest request, RestoreCommitHandler handler, CancellationToken ct) =>
        {
            if (!string.Equals(request.ContentType, "application/zip", StringComparison.OrdinalIgnoreCase))
            {
                return Results.Problem(statusCode: 415, title: "Expected application/zip");
            }

            try
            {
                return Results.Ok(await handler.HandleAsync(request.Body, ct));
            }
            catch (RestoreConflictException)
            {
                return Results.Problem(statusCode: 409, title: "Restore conflict");
            }
            catch (Exception e) when (e is InvalidDataException or JsonException or NotSupportedException or ArgumentException)
            {
                return Results.Problem(statusCode: 400, title: "Invalid restore archive");
            }

        }).WithTags("Restore").WithMetadata(new RequestSizeLimitAttribute(RestorePreviewHandler.MaxArchive)).Accepts<Stream>("application/zip").Produces<RestoreCommitResult>().Produces<ProblemDetails>(400).Produces<ProblemDetails>(409).Produces<ProblemDetails>(413).Produces<ProblemDetails>(415).Produces<ProblemDetails>(500);
    }
}
