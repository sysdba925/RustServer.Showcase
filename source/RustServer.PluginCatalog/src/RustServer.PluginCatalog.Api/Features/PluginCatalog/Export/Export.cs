using Microsoft.AspNetCore.Mvc;
using RustServer.PluginCatalog.Api.Features.PluginCatalog.Ports;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RustServer.PluginCatalog.Api.Features.PluginCatalog.Export;

public sealed record ExportPlugin(Guid Id, string Name, string Slug, string? Description, string? Author, string? Notes, string? SourceDescription, decimal? Price, string? Currency, string? CurrentKnownVersion, DateOnly? LastPublishedDate, Guid? IconMediaId, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, Guid[] TagIds, Guid[] FolderIds);
public sealed record ExportSite(Guid Id, string Name, string? BaseUrl);
public sealed record ExportTag(Guid Id, string Name, string Color);
public sealed record ExportFolder(Guid Id, string Name);
public sealed record ExportSource(Guid Id, Guid PluginId, Guid? SiteId, string Url, string? Label);
public sealed record ExportMedia(Guid Id, Guid PluginId, string Kind, string? Caption, int SortOrder, string? StorageKey, string? ExternalUrl, string? ContentType, string? ArchivePath);
public sealed record ExportCatalog(ExportPlugin[] Plugins, ExportSite[] Sites, ExportTag[] Tags, ExportFolder[] Folders, ExportSource[] Sources, ExportMedia[] Media);
public interface IExportStore
{
    Task<ExportCatalog> ReadSnapshotAsync(CancellationToken ct);
}

public sealed class ExportHandler(IExportStore store, IPluginMediaStorage storage)
{
    private readonly IExportStore _store = store;
    private readonly IPluginMediaStorage _storage = storage;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task<Stream> HandleAsync(CancellationToken ct)
    {
        var catalog = await _store.ReadSnapshotAsync(ct);
        var path = Path.Combine(Path.GetTempPath(), $"plugin-catalog-export-{Guid.NewGuid():N}.tmp");
        var file = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81920,
            FileOptions.Asynchronous | FileOptions.DeleteOnClose);
        try
        {
            using (var zip = new ZipArchive(file, ZipArchiveMode.Create, leaveOpen: true))
            {
                await WriteJsonAsync(zip, "manifest.json", new { formatVersion = 2 }, ct);
                await WriteJsonAsync(zip, "catalog.json", catalog, ct);
                foreach (var media in catalog.Media.Where(m => m.StorageKey is not null))
                {
                    var content = await _storage.OpenReadAsync(media.StorageKey!, PluginMediaStorageVariant.Original, media.ContentType ?? "application/octet-stream", ct);
                    if (content is null)
                    {
                        throw new PluginMediaStorageException(PluginMediaStorageFailureKind.Unavailable, "A media original is unavailable.");
                    }

                    await using var source = content.Stream;
                    await using var target = zip.CreateEntry(media.ArchivePath!, CompressionLevel.Optimal).Open();
                    await source.CopyToAsync(target, ct);
                }
            }
            file.Position = 0;
            return file;
        }
        catch
        {
            await file.DisposeAsync();
            throw;
        }
    }

    private static async Task WriteJsonAsync<T>(ZipArchive zip, string name, T value, CancellationToken ct)
    {
        await using var entry = zip.CreateEntry(name, CompressionLevel.Optimal).Open();
        await JsonSerializer.SerializeAsync(entry, value, JsonOptions, ct);
    }
}

public static class ExportEndpoints
{
    public static void MapExportEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/export",
            async (ExportHandler handler, CancellationToken ct) =>
            {
                try
                {
                    return Results.File(
                        await handler.HandleAsync(ct), "application/zip", "plugin-catalog.zip");
                }
                catch (Exception e) when (e is PluginMediaStorageException or IOException or UnauthorizedAccessException)
                {
                    return Results.Problem(statusCode: 503, title: "Export unavailable", detail: "The archive could not be assembled completely.");
                }
            })
                .WithTags("Export")
                .Produces(200, contentType: "application/zip")
                .Produces<ProblemDetails>(503);
    }
}
