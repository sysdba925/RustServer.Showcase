using RustServer.PluginCatalog.Api.Features.PluginCatalog.Export;
using RustServer.PluginCatalog.Api.Features.PluginCatalog.Ports;
using RustServer.PluginCatalog.Api.Features.PluginCatalog.Restore;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace RustServer.PluginCatalog.Api.Tests;

public sealed class ExportTests
{
    [Fact]
    public async Task RestoreRejectsUnsafeArchivePath()
    {
        using var file = new MemoryStream();
        using (var zip = new ZipArchive(file, ZipArchiveMode.Create, leaveOpen: true))
        {
            zip.CreateEntry("../outside");
        }

        file.Position = 0;
        var catalog = new ExportCatalog([], [], [], [], [], []);
        await Assert.ThrowsAsync<InvalidDataException>(() => new RestorePreviewHandler(new TestStore(catalog)).HandleAsync(file, CancellationToken.None));
    }

    [Fact]
    public async Task ExportIncludesCatalogAndOriginalAndRejectsMissingFile()
    {
        var pluginId = Guid.NewGuid();
        var mediaId = Guid.NewGuid();
        var catalog = new ExportCatalog(
            [new(pluginId, "Example", "example", null, null, null, null, null, null, null, null, mediaId, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, [], [])],
            [], [], [], [],
            [new(mediaId, pluginId, "Icon", null, 0, "logical/key/original.png", null, "image/png", $"media/{mediaId:D}"),
             new(Guid.NewGuid(), pluginId, "Video", null, 1, null, "https://example.com/video", null, null)]);
        var storage = new TestStorage();
        var handler = new ExportHandler(new TestStore(catalog), storage);
        await using var file = await handler.HandleAsync(CancellationToken.None);
        using var zip = new ZipArchive(file, ZipArchiveMode.Read, leaveOpen: true);
        using var manifest = JsonDocument.Parse(zip.GetEntry("manifest.json")!.Open());
        Assert.Equal(1, manifest.RootElement.GetProperty("formatVersion").GetInt32());
        using var data = JsonDocument.Parse(zip.GetEntry("catalog.json")!.Open());
        Assert.Equal(pluginId, data.RootElement.GetProperty("plugins")[0].GetProperty("id").GetGuid());
        Assert.Equal("https://example.com/video", data.RootElement.GetProperty("media")[1].GetProperty("externalUrl").GetString());
        using var reader = new StreamReader(zip.GetEntry($"media/{mediaId:D}")!.Open());
        Assert.Equal("original", await reader.ReadToEndAsync());
        reader.Dispose();
        zip.Dispose();
        file.Position = 0;
        var preview = await new RestorePreviewHandler(new TestStore(catalog)).HandleAsync(file, CancellationToken.None);
        Assert.Equal(1, preview.Counts.Plugins);
        Assert.Equal(2, preview.Counts.Media);
        Assert.Contains(preview.Conflicts, c => c.Type == "plugin" && c.Id == pluginId && c.Reason == "id");
        Assert.Contains(preview.Conflicts, c => c.Type == "plugin" && c.Reason == "slug");
        storage.Missing = true;
        await Assert.ThrowsAsync<PluginMediaStorageException>(() => handler.HandleAsync(CancellationToken.None));
    }

    private sealed class TestStore(ExportCatalog catalog) : IExportStore
    {
        public Task<ExportCatalog> ReadSnapshotAsync(CancellationToken ct) => Task.FromResult(catalog);
    }
    private sealed class TestStorage : IPluginMediaStorage
    {
        public bool Missing { get; set; }
        public Task<PluginMediaContent?> OpenReadAsync(string key, PluginMediaStorageVariant variant, string contentType, CancellationToken ct) =>
            Task.FromResult<PluginMediaContent?>(Missing ? null : new(new MemoryStream(Encoding.UTF8.GetBytes("original")), contentType));
        public Task<StoredPluginMedia> SaveAsync(PluginMediaStorageWriteRequest request, CancellationToken ct) => throw new NotSupportedException();
        public Task DeleteAsync(string key, CancellationToken ct) => throw new NotSupportedException();
    }
}
