using Microsoft.AspNetCore.Mvc;
using RustServer.PluginCatalog.Api.Features.PluginCatalog.Export;
using System.IO.Compression;
using System.Text.Json;

namespace RustServer.PluginCatalog.Api.Features.PluginCatalog.Restore;

public sealed record RestoreCounts(int Plugins, int Sites, int Tags, int Folders, int Sources, int Media);
public sealed record RestoreConflict(string Type, Guid Id, string Reason);
public sealed record RestorePreview(RestoreCounts Counts, IReadOnlyList<RestoreConflict> Conflicts);

public sealed class RestorePreviewHandler(IExportStore store)
{
    public const int MaxArchive = 64 * 1024 * 1024;
    private const long MaxExpanded = 256L * 1024 * 1024;
    private const int MaxJson = 16 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        RespectRequiredConstructorParameters = true
    };

    public async Task<RestorePreview> HandleAsync(Stream input, CancellationToken ct)
    {
        var (catalog, _) = await ValidateArchiveAsync(input, ct);
        var current = await store.ReadSnapshotAsync(ct);
        var conflicts = new List<RestoreConflict>();
        AddConflicts("plugin", catalog.Plugins.Select(x => x.Id), current.Plugins.Select(x => x.Id), conflicts);
        AddConflicts("site", catalog.Sites.Select(x => x.Id), current.Sites.Select(x => x.Id), conflicts);
        AddConflicts("tag", catalog.Tags.Select(x => x.Id), current.Tags.Select(x => x.Id), conflicts);
        AddConflicts("folder", catalog.Folders.Select(x => x.Id), current.Folders.Select(x => x.Id), conflicts);
        AddConflicts("source", catalog.Sources.Select(x => x.Id), current.Sources.Select(x => x.Id), conflicts);
        AddConflicts("media", catalog.Media.Select(x => x.Id), current.Media.Select(x => x.Id), conflicts);
        var slugs = current.Plugins.Select(x => x.Slug).ToHashSet(StringComparer.Ordinal);
        conflicts.AddRange(catalog.Plugins.Where(x => slugs.Contains(x.Slug)).Select(x => new RestoreConflict("plugin", x.Id, "slug")));
        return new(Counts(catalog), conflicts);
    }

    public static RestoreCounts Counts(ExportCatalog c)
    {
        return new(c.Plugins.Length, c.Sites.Length, c.Tags.Length, c.Folders.Length, c.Sources.Length, c.Media.Length);
    }

    public static async Task<(ExportCatalog Catalog, byte[] Archive)> ValidateArchiveAsync(Stream input, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        await CopyLimitedAsync(input, buffer, MaxArchive, ct);
        buffer.Position = 0;
        using var zip = new ZipArchive(buffer, ZipArchiveMode.Read);
        if (zip.Entries.Count > 10000)
        {
            throw new InvalidDataException();
        }

        var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.Ordinal);
        long total = 0;
        foreach (var entry in zip.Entries)
        {
            var name = entry.FullName;
            if (name.Length == 0 || name.Contains('\\') || name.StartsWith('/') ||
                name.Split('/').Any(part => part is "" or "." or "..") || name.Contains(':') ||
                !entries.TryAdd(name, entry) || entry.Length > MaxExpanded - total)
            {
                throw new InvalidDataException();
            }

            total += entry.Length;
        }

        if (!entries.TryGetValue("manifest.json", out var manifestEntry) || !entries.TryGetValue("catalog.json", out var catalogEntry))
        {
            throw new InvalidDataException();
        }

        using var manifest = JsonDocument.Parse(await ReadJsonAsync(manifestEntry, ct));
        if (manifest.RootElement.ValueKind != JsonValueKind.Object || !manifest.RootElement.TryGetProperty("formatVersion", out var version) || !version.TryGetInt32(out var v) || v != 2)
        {
            throw new InvalidDataException();
        }

        var catalog = JsonSerializer.Deserialize<ExportCatalog>(await ReadJsonAsync(catalogEntry, ct), JsonOptions) ?? throw new InvalidDataException();
        Validate(catalog, entries);
        foreach (var entry in zip.Entries.Where(e => e.FullName is not "manifest.json" and not "catalog.json"))
        {
            await using var stream = entry.Open();
            if (await CopyLimitedAsync(stream, Stream.Null, entry.Length, ct) != entry.Length)
            {
                throw new InvalidDataException();
            }
        }

        return (catalog, buffer.ToArray());
    }

    private static void Validate(ExportCatalog c, Dictionary<string, ZipArchiveEntry> entries)
    {
        if (c.Plugins is null || c.Sites is null || c.Tags is null || c.Folders is null || c.Sources is null || c.Media is null ||
            c.Plugins.Any(x => x is null) || c.Sites.Any(x => x is null) || c.Tags.Any(x => x is null) || c.Folders.Any(x => x is null) || c.Sources.Any(x => x is null) || c.Media.Any(x => x is null))
        {
            throw new InvalidDataException();
        }

        var plugins = Ids(c.Plugins.Select(x => x.Id));
        var sites = Ids(c.Sites.Select(x => x.Id));
        var tags = Ids(c.Tags.Select(x => x.Id));
        var folders = Ids(c.Folders.Select(x => x.Id));
        Ids(c.Sources.Select(x => x.Id));
        var media = Ids(c.Media.Select(x => x.Id));
        if (c.Plugins.Any(p => string.IsNullOrWhiteSpace(p.Name) || string.IsNullOrWhiteSpace(p.Slug)) || c.Plugins.Select(p => p.Slug).Distinct(StringComparer.Ordinal).Count() != c.Plugins.Length || c.Sites.Any(s => string.IsNullOrWhiteSpace(s.Name)) || c.Tags.Any(t => string.IsNullOrWhiteSpace(t.Name) || string.IsNullOrWhiteSpace(t.Color)) || c.Folders.Any(f => string.IsNullOrWhiteSpace(f.Name)) || c.Sources.Any(s => string.IsNullOrWhiteSpace(s.Url)) || c.Media.Any(m => string.IsNullOrWhiteSpace(m.Kind)))
        {
            throw new InvalidDataException();
        }

        if (c.Plugins.Any(p => p.TagIds is null || p.FolderIds is null || p.TagIds.Distinct().Count() != p.TagIds.Length || p.FolderIds.Distinct().Count() != p.FolderIds.Length || p.TagIds.Any(id => !tags.Contains(id)) || p.FolderIds.Any(id => !folders.Contains(id)) || (p.IconMediaId is { } id && (!media.Contains(id) || !c.Media.Any(m => m.Id == id && m.PluginId == p.Id && m.Kind == "Icon")))))
        {
            throw new InvalidDataException();
        }

        if (c.Sources.Any(s => !plugins.Contains(s.PluginId) || (s.SiteId is { } id && !sites.Contains(id))))
        {
            throw new InvalidDataException();
        }

        foreach (var m in c.Media)
        {
            if (!plugins.Contains(m.PluginId))
            {
                throw new InvalidDataException();
            }

            if (m.StorageKey is not null)
            {
                var path = $"media/{m.Id:D}";
                if (m.StorageKey.Length == 0 || m.StorageKey.Contains('\\') || m.StorageKey.Contains(':') || m.StorageKey.Split('/').Any(p => p is "" or "." or "..") || m.ArchivePath != path || m.ExternalUrl is not null || !entries.Remove(path))
                {
                    throw new InvalidDataException();
                }
            }
            else if (m.ArchivePath is not null || string.IsNullOrWhiteSpace(m.ExternalUrl))
            {
                throw new InvalidDataException();
            }
        }

        entries.Remove("manifest.json"); entries.Remove("catalog.json");
        if (entries.Count != 0)
        {
            throw new InvalidDataException();
        }
    }

    private static HashSet<Guid> Ids(IEnumerable<Guid> ids)
    {
        var result = new HashSet<Guid>();
        foreach (var id in ids)
        {
            if (id == Guid.Empty || !result.Add(id))
            {
                throw new InvalidDataException();
            }
        }

        return result;
    }
    private static void AddConflicts(string type, IEnumerable<Guid> imported, IEnumerable<Guid> current, List<RestoreConflict> conflicts)
    {
        var existing = current.ToHashSet();
        conflicts.AddRange(imported.Where(existing.Contains).Select(id => new RestoreConflict(type, id, "id")));
    }
    private static async Task<byte[]> ReadJsonAsync(ZipArchiveEntry entry, CancellationToken ct)
    {
        if (entry.Length > MaxJson)
        {
            throw new InvalidDataException();
        }

        await using var source = entry.Open();
        using var target = new MemoryStream();
        if (await CopyLimitedAsync(source, target, MaxJson, ct) != entry.Length)
        {
            throw new InvalidDataException();
        }

        return target.ToArray();
    }
    private static async Task<long> CopyLimitedAsync(Stream source, Stream target, long limit, CancellationToken ct)
    {
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, ct)) != 0)
        {
            total += read;
            if (total > limit)
            {
                throw new InvalidDataException();
            }

            await target.WriteAsync(buffer.AsMemory(0, read), ct);
        }

        return total;
    }
}

public static class RestorePreviewEndpoints
{
    public static void MapRestorePreviewEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/restore/preview", async (HttpRequest request, RestorePreviewHandler handler, CancellationToken ct) =>
        {
            if (!string.Equals(request.ContentType, "application/zip", StringComparison.OrdinalIgnoreCase))
            {
                return Results.Problem(statusCode: 415, title: "Expected application/zip");
            }

            try
            {
                return Results.Ok(await handler.HandleAsync(request.Body, ct));
            }
            catch (Exception e) when (e is InvalidDataException or JsonException or NotSupportedException or ArgumentException or IOException or InvalidOperationException)
            {
                return Results.Problem(statusCode: 400, title: "Invalid restore archive");
            }
        }).WithTags("Restore").WithMetadata(new RequestSizeLimitAttribute(RestorePreviewHandler.MaxArchive)).Accepts<Stream>("application/zip").Produces<RestorePreview>().Produces<ProblemDetails>(400).Produces<ProblemDetails>(413).Produces<ProblemDetails>(415);
    }
}
