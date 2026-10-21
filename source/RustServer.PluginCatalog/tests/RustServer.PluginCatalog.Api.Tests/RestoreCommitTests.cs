using ImageMagick;
using Microsoft.Extensions.Options;
using RustServer.PluginCatalog.Api.Features.PluginCatalog.Export;
using RustServer.PluginCatalog.Api.Features.PluginCatalog.Ports;
using RustServer.PluginCatalog.Api.Features.PluginCatalog.Restore;
using RustServer.PluginCatalog.Api.Infrastructure.MediaStorage;
using RustServer.PluginCatalog.Api.Infrastructure.MediaStorage.Local;

namespace RustServer.PluginCatalog.Api.Tests;

public sealed class RestoreCommitTests
{
    [Fact]
    public async Task RestorePreservesArchiveAndCleansFilesOnFailure()
    {
        var pluginId = Guid.NewGuid();
        var mediaId = Guid.NewGuid();
        var key = $"{pluginId:D}/{mediaId:D}/original.png";
        var now = DateTimeOffset.UtcNow;
        var catalog = new ExportCatalog(
            [new(pluginId, "Example", "example", null, null, null, null, null, null, null, null, mediaId, now, now, [], [])],
            [], [], [], [], [new(mediaId, pluginId, "Icon", null, 0, key, null, "image/png", $"media/{mediaId:D}")]);
        var root = Path.Combine(Path.GetTempPath(), $"catalog-restore-test-{Guid.NewGuid():N}");
        try
        {
            var media = new LocalPluginMediaStorage(Options.Create(new MediaStorageOptions { Provider = "Local", RootPath = root }));
            var store = new TestRestoreStore { Fail = true };
            var handler = new RestoreCommitHandler(store, media);
            await using var archive = await new ExportHandler(new ExportStore(catalog), new ExportMediaStorage()).HandleAsync(CancellationToken.None);
            await Assert.ThrowsAsync<InvalidOperationException>(() => handler.HandleAsync(archive, CancellationToken.None));
            Assert.Null(store.Saved);
            Assert.Null(await media.OpenReadAsync(key, PluginMediaStorageVariant.Original, "image/png", CancellationToken.None));
            Assert.Null(await media.OpenReadAsync(key, PluginMediaStorageVariant.Thumbnail, "image/png", CancellationToken.None));
            store.Fail = false;
            archive.Position = 0;
            var result = await handler.HandleAsync(archive, CancellationToken.None);
            Assert.Equal(1, result.Counts.Media);
            Assert.Equal(pluginId, store.Saved!.Plugins[0].Id);
            Assert.Equal(mediaId, store.Saved.Plugins[0].IconMediaId);
            var original = await media.OpenReadAsync(key, PluginMediaStorageVariant.Original, "image/png", CancellationToken.None);
            await using (original!.Stream)
            {
                using var copy = new MemoryStream(); await original.Stream.CopyToAsync(copy); Assert.Equal(CreatePng(), copy.ToArray());
            }

            archive.Position = 0;
            await Assert.ThrowsAsync<RestoreConflictException>(() => handler.HandleAsync(archive, CancellationToken.None));
            archive.Position = 0;
            await Assert.ThrowsAsync<RestoreConflictException>(() => new RestoreCommitHandler(new TestRestoreStore(), media).HandleAsync(archive, CancellationToken.None));
            var preserved = await media.OpenReadAsync(key, PluginMediaStorageVariant.Original, "image/png", CancellationToken.None);
            await using (preserved!.Stream)
            {
                using var copy = new MemoryStream(); await preserved.Stream.CopyToAsync(copy); Assert.Equal(CreatePng(), copy.ToArray());
            }
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task RestoreCreatesReadableThumbnail()
    {
        var pluginId = Guid.NewGuid();
        var mediaId = Guid.NewGuid();
        var key = $"{pluginId:D}/{mediaId:D}/original.png";
        var now = DateTimeOffset.UtcNow;
        var catalog = new ExportCatalog(
            [new(pluginId, "Example", "example", null, null, null, null, null, null, null, null, mediaId, now, now, [], [])],
            [], [], [], [], [new(mediaId, pluginId, "Icon", null, 0, key, null, "image/png", $"media/{mediaId:D}")]);
        var root = Path.Combine(Path.GetTempPath(), $"catalog-restore-test-{Guid.NewGuid():N}");
        try
        {
            var media = new LocalPluginMediaStorage(Options.Create(new MediaStorageOptions { Provider = "Local", RootPath = root }));
            await using var archive = await new ExportHandler(new ExportStore(catalog), new ExportMediaStorage()).HandleAsync(CancellationToken.None);
            await new RestoreCommitHandler(new TestRestoreStore(), media).HandleAsync(archive, CancellationToken.None);
            var thumbnail = await media.OpenReadAsync(key, PluginMediaStorageVariant.Thumbnail, "image/png", CancellationToken.None);
            Assert.NotNull(thumbnail);
            Assert.Equal("image/webp", thumbnail.ContentType);
            await using var stream = thumbnail.Stream;
            using var image = new MagickImage(stream);
            Assert.Equal(MagickFormat.WebP, image.Format);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static byte[] CreatePng()
    {
        using var image = new MagickImage(MagickColors.DodgerBlue, 1, 1);
        image.Format = MagickFormat.Png;
        return image.ToByteArray();
    }

    private sealed class TestRestoreStore : IRestoreStore
    {
        public bool Fail { get; set; }
        public ExportCatalog? Saved { get; private set; }
        public async Task RestoreAsync(ExportCatalog catalog, Func<CancellationToken, Task> writeMedia, CancellationToken ct)
        {
            if (Saved is not null)
            {
                throw new RestoreConflictException();
            }

            await writeMedia(ct);
            if (Fail)
            {
                throw new InvalidOperationException("Simulated database failure");
            }

            Saved = catalog;
        }
    }
    private sealed class ExportStore(ExportCatalog catalog) : IExportStore
    {
        public Task<ExportCatalog> ReadSnapshotAsync(CancellationToken ct) => Task.FromResult(catalog);
    }
    private sealed class ExportMediaStorage : IPluginMediaStorage
    {
        public Task<PluginMediaContent?> OpenReadAsync(string key, PluginMediaStorageVariant variant, string contentType, CancellationToken ct) => Task.FromResult<PluginMediaContent?>(new(new MemoryStream(CreatePng()), contentType));
        public Task<StoredPluginMedia> SaveAsync(PluginMediaStorageWriteRequest request, CancellationToken ct) => throw new NotSupportedException();
        public Task DeleteAsync(string key, CancellationToken ct) => throw new NotSupportedException();
    }
}
