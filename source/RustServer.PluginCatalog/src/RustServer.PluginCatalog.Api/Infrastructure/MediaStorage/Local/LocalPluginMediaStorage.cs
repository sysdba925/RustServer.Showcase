using ImageMagick;
using Microsoft.Extensions.Options;
using RustServer.PluginCatalog.Api.Features.PluginCatalog.Ports;
using RustServer.PluginCatalog.Api.Features.PluginCatalog.Restore;
using System.Buffers;

namespace RustServer.PluginCatalog.Api.Infrastructure.MediaStorage.Local;

public sealed class LocalPluginMediaStorage(IOptions<MediaStorageOptions> options) : IPluginMediaStorage, IRestoreMediaStorage
{
    private const string ThumbnailFileName = "thumbnail.webp";
    private const int BufferSize = 80 * 1024;

    public async Task CreateOriginalAsync(string storageKey, Stream content, CancellationToken ct)
    {
        var configuration = GetValidatedOptions();
        var root = GetRootPath(configuration, create: true);
        var path = GetFilePath(root, storageKey, createDirectory: true);
        var thumbnailPath = Path.Combine(Path.GetDirectoryName(path)!, ThumbnailFileName);
        var thumbnailTempPath = Path.Combine(Path.GetDirectoryName(path)!, $".thumbnail-{Guid.NewGuid():N}.tmp");
        FileStream target;
        try
        {
            target = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, BufferSize, FileOptions.Asynchronous);
        }
        catch (IOException) when (File.Exists(path) || Directory.Exists(path))
        {
            throw new RestoreConflictException();
        }
        try
        {
            await using (target)
            {
                await content.CopyToAsync(target, ct);
                await target.FlushAsync(ct);
            }
            var format = DetectFormat(path);
            CreateValidatedThumbnail(path, thumbnailTempPath, format, configuration);
            try
            {
                File.Move(thumbnailTempPath, thumbnailPath, overwrite: false);
            }
            catch (IOException) when (File.Exists(thumbnailPath) || Directory.Exists(thumbnailPath))
            {
                throw new RestoreConflictException();
            }
        }
        catch (Exception error)
        {
            var failures = new List<Exception>();
            foreach (var createdPath in new[] { path, thumbnailTempPath })
            {
                try { File.Delete(createdPath); }
                catch (Exception cleanup) { failures.Add(cleanup); }
            }
            if (failures.Count > 0)
            {
                throw new AggregateException("Incomplete media cleanup.", [error, .. failures]);
            }

            throw;
        }
    }

    public Task DeleteCreatedOriginalAsync(string storageKey, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var root = GetRootPath(GetValidatedOptions(), create: false);
        var path = GetFilePath(root, storageKey, createDirectory: false);
        File.Delete(path);
        File.Delete(Path.Combine(Path.GetDirectoryName(path)!, ThumbnailFileName));
        return Task.CompletedTask;
    }

    public async Task<StoredPluginMedia> SaveAsync(
        PluginMediaStorageWriteRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Content);

        var configuration = GetValidatedOptions();
        string? uploadTempPath = null;
        string? thumbnailTempPath = null;
        try
        {
            var rootPath = GetRootPath(configuration, create: true);
            uploadTempPath = await CopyToTemporaryFileAsync(
                request.Content,
                rootPath,
                configuration.MaxUploadBytes,
                cancellationToken);

            var format = DetectFormat(uploadTempPath);
            var storageKey = $"{request.PluginId:D}/{request.MediaId:D}/original.{format.Extension}";
            var mediaDirectory = GetMediaDirectory(rootPath, storageKey, create: true);
            var originalPath = GetFilePath(rootPath, storageKey, createDirectory: true);
            var thumbnailPath = Path.Combine(mediaDirectory, ThumbnailFileName);

            thumbnailTempPath = Path.Combine(mediaDirectory, $".thumbnail-{Guid.NewGuid():N}.tmp");
            CreateValidatedThumbnail(uploadTempPath, thumbnailTempPath, format, configuration);

            File.Move(uploadTempPath, originalPath, overwrite: false);
            uploadTempPath = null;

            try
            {
                File.Move(thumbnailTempPath, thumbnailPath, overwrite: false);
                thumbnailTempPath = null;
            }
            catch
            {
                TryDeleteFile(originalPath);
                throw;
            }

            return new StoredPluginMedia(storageKey, format.ContentType);
        }
        catch (PluginMediaStorageException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (IOException exception)
        {
            throw Unavailable(exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw Unavailable(exception);
        }
        finally
        {
            if (uploadTempPath is not null)
            {
                TryDeleteFile(uploadTempPath);
            }

            if (thumbnailTempPath is not null)
            {
                TryDeleteFile(thumbnailTempPath);
            }
        }
    }

    public Task<PluginMediaContent?> OpenReadAsync(
        string storageKey,
        PluginMediaStorageVariant variant,
        string originalContentType,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var configuration = GetValidatedOptions();
            var rootPath = GetRootPath(configuration, create: false);
            var originalPath = GetFilePath(rootPath, storageKey, createDirectory: false);
            var filePath = variant == PluginMediaStorageVariant.Thumbnail
                ? Path.Combine(Path.GetDirectoryName(originalPath)!, ThumbnailFileName)
                : originalPath;

            if (!File.Exists(filePath))
            {
                return Task.FromResult<PluginMediaContent?>(null);
            }

            var stream = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                BufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            return Task.FromResult<PluginMediaContent?>(new PluginMediaContent(
                stream,
                variant == PluginMediaStorageVariant.Thumbnail ? "image/webp" : originalContentType));
        }
        catch (PluginMediaStorageException)
        {
            throw;
        }
        catch (IOException exception)
        {
            throw Unavailable(exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw Unavailable(exception);
        }
    }

    public Task DeleteAsync(string storageKey, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var configuration = GetValidatedOptions();
            var rootPath = GetRootPath(configuration, create: false);
            var originalPath = GetFilePath(rootPath, storageKey, createDirectory: false);
            var mediaDirectory = Path.GetDirectoryName(originalPath)!;

            if (File.Exists(originalPath))
            {
                File.Delete(originalPath);
            }

            var thumbnailPath = Path.Combine(mediaDirectory, ThumbnailFileName);
            if (File.Exists(thumbnailPath))
            {
                File.Delete(thumbnailPath);
            }

            if (Directory.Exists(mediaDirectory))
            {
                EnsureNoReparsePoint(mediaDirectory);
                Directory.Delete(mediaDirectory, recursive: false);
            }

            return Task.CompletedTask;
        }
        catch (PluginMediaStorageException)
        {
            throw;
        }
        catch (IOException exception)
        {
            throw Unavailable(exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw Unavailable(exception);
        }
    }

    private static async Task<string> CopyToTemporaryFileAsync(
        Stream source,
        string rootPath,
        long maxUploadBytes,
        CancellationToken cancellationToken)
    {
        var tempDirectory = EnsureDirectory(rootPath, [".tmp"]);
        var tempPath = Path.Combine(tempDirectory, $"{Guid.NewGuid():N}.tmp");
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        try
        {
            await using var target = new FileStream(
                tempPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                BufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            long written = 0;
            while (true)
            {
                var read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
                if (read == 0)
                {
                    break;
                }

                if (written > maxUploadBytes - read)
                {
                    throw new PluginMediaStorageException(
                        PluginMediaStorageFailureKind.UploadTooLarge,
                        "The uploaded file exceeds the configured media size limit.");
                }

                await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                written += read;
            }

            await target.FlushAsync(cancellationToken);
            return tempPath;
        }
        catch
        {
            TryDeleteFile(tempPath);
            throw;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static DetectedImageFormat DetectFormat(string filePath)
    {
        var header = new byte[12];
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, header.Length, FileOptions.SequentialScan);
        var length = 0;
        while (length < header.Length)
        {
            var read = stream.Read(header, length, header.Length - length);
            if (read == 0)
            {
                break;
            }

            length += read;
        }

        if (length >= 8
            && header.AsSpan(0, 8).SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
        {
            return new DetectedImageFormat(MagickFormat.Png, "png", "image/png");
        }

        if (length >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
        {
            return new DetectedImageFormat(MagickFormat.Jpeg, "jpg", "image/jpeg");
        }

        if (length >= 6
            && ((header.AsSpan(0, 6).SequenceEqual("GIF87a"u8))
                || header.AsSpan(0, 6).SequenceEqual("GIF89a"u8)))
        {
            return new DetectedImageFormat(MagickFormat.Gif, "gif", "image/gif");
        }

        if (length >= 12
            && header.AsSpan(0, 4).SequenceEqual("RIFF"u8)
            && header.AsSpan(8, 4).SequenceEqual("WEBP"u8))
        {
            return new DetectedImageFormat(MagickFormat.WebP, "webp", "image/webp");
        }

        throw new PluginMediaStorageException(
            PluginMediaStorageFailureKind.UnsupportedMediaType,
            "The uploaded file does not have a supported image signature.");
    }

    private static void CreateValidatedThumbnail(
        string originalPath,
        string thumbnailPath,
        DetectedImageFormat format,
        MediaStorageOptions options)
    {
        try
        {
            var settings = new MagickReadSettings { Format = format.MagickFormat };
            using var images = new MagickImageCollection();
            images.Ping(originalPath, settings);
            ValidateImageLimits(images, options);

            images.Read(originalPath, settings);
            ValidateImageLimits(images, options);

            using var thumbnail = images[0].Clone();
            thumbnail.AutoOrient();
            thumbnail.Resize(new MagickGeometry(options.ThumbnailMaxDimension, options.ThumbnailMaxDimension)
            {
                Greater = true
            });
            thumbnail.Strip();
            thumbnail.Format = MagickFormat.WebP;
            thumbnail.Quality = 80;
            thumbnail.Write(thumbnailPath);
        }
        catch (PluginMediaStorageException)
        {
            throw;
        }
        catch (MagickException exception)
        {
            throw new PluginMediaStorageException(
                PluginMediaStorageFailureKind.InvalidImage,
                "The uploaded file could not be decoded as the claimed image format.",
                exception);
        }
        catch (OverflowException exception)
        {
            throw new PluginMediaStorageException(
                PluginMediaStorageFailureKind.InvalidImage,
                "The uploaded image dimensions are invalid.",
                exception);
        }
    }

    private static void ValidateImageLimits(MagickImageCollection images, MediaStorageOptions options)
    {
        if (images.Count == 0 || images.Count > options.MaxFrameCount)
        {
            throw new PluginMediaStorageException(
                PluginMediaStorageFailureKind.InvalidImage,
                "The uploaded image exceeds the configured frame limit.");
        }

        ulong totalPixels = 0;
        foreach (var image in images)
        {
            totalPixels = checked(totalPixels + checked((ulong) image.Width * image.Height));
            if (totalPixels > options.MaxDecodedPixels)
            {
                throw new PluginMediaStorageException(
                    PluginMediaStorageFailureKind.InvalidImage,
                    "The uploaded image exceeds the configured decoded pixel limit.");
            }
        }
    }

    private MediaStorageOptions GetValidatedOptions()
    {
        var value = options.Value;
        if (!string.Equals(value.Provider, "Local", StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(value.RootPath)
            || value.MaxUploadBytes <= 0
            || value.MaxDecodedPixels == 0
            || value.MaxFrameCount <= 0
            || value.ThumbnailMaxDimension == 0)
        {
            throw new PluginMediaStorageException(
                PluginMediaStorageFailureKind.Unavailable,
                "Local media storage is not configured.");
        }

        return value;
    }

    private static string GetRootPath(MediaStorageOptions options, bool create)
    {
        var rootPath = Path.GetFullPath(options.RootPath!);
        if (create)
        {
            EnsureDirectory(rootPath, []);
        }
        else
        {
            EnsureNoReparsePointsToRoot(rootPath);
        }

        return rootPath;
    }

    private static string GetFilePath(string rootPath, string storageKey, bool createDirectory)
    {
        var segments = ValidateStorageKey(storageKey);
        var directorySegments = segments[..^1];
        var directory = createDirectory
            ? EnsureDirectory(rootPath, directorySegments)
            : GetExistingDirectory(rootPath, directorySegments);

        var path = Path.GetFullPath(Path.Combine([directory, segments[^1]]));
        if (!IsWithinRoot(rootPath, path))
        {
            throw new PluginMediaStorageException(
                PluginMediaStorageFailureKind.Unavailable,
                "The media storage key is not contained by the configured root.");
        }

        return path;
    }

    private static string GetMediaDirectory(string rootPath, string storageKey, bool create)
    {
        var segments = ValidateStorageKey(storageKey);
        return create
            ? EnsureDirectory(rootPath, segments[..^1])
            : GetExistingDirectory(rootPath, segments[..^1]);
    }

    private static string EnsureDirectory(string rootPath, IReadOnlyList<string> segments)
    {
        Directory.CreateDirectory(rootPath);
        EnsureNoReparsePointsToRoot(rootPath);

        var current = rootPath;
        foreach (var segment in segments)
        {
            current = Path.Combine(current, segment);
            Directory.CreateDirectory(current);
            EnsureNoReparsePoint(current);
        }

        return current;
    }

    private static string GetExistingDirectory(string rootPath, IReadOnlyList<string> segments)
    {
        EnsureNoReparsePointsToRoot(rootPath);

        var current = rootPath;
        foreach (var segment in segments)
        {
            current = Path.Combine(current, segment);
            if (Directory.Exists(current))
            {
                EnsureNoReparsePoint(current);
            }
        }

        return current;
    }

    private static void EnsureNoReparsePointsToRoot(string rootPath)
    {
        for (var current = new DirectoryInfo(rootPath); current is not null; current = current.Parent)
        {
            if (current.Exists)
            {
                EnsureNoReparsePoint(current.FullName);
            }
        }
    }

    private static void EnsureNoReparsePoint(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new PluginMediaStorageException(
                PluginMediaStorageFailureKind.Unavailable,
                "The configured media path contains a symbolic link or reparse point.");
        }
    }

    private static string[] ValidateStorageKey(string storageKey)
    {
        if (string.IsNullOrWhiteSpace(storageKey)
            || Path.IsPathRooted(storageKey)
            || storageKey.StartsWith('/')
            || storageKey.StartsWith('\\')
            || storageKey.Contains('\\')
            || storageKey.Contains(':'))
        {
            throw new PluginMediaStorageException(
                PluginMediaStorageFailureKind.Unavailable,
                "The media storage key is invalid.");
        }

        var segments = storageKey.Split('/', StringSplitOptions.None);
        if (segments.Length < 3
            || segments.Any(segment => string.IsNullOrWhiteSpace(segment) || segment is "." or ".."))
        {
            throw new PluginMediaStorageException(
                PluginMediaStorageFailureKind.Unavailable,
                "The media storage key is invalid.");
        }

        return segments;
    }

    private static bool IsWithinRoot(string rootPath, string candidatePath)
    {
        var rootWithSeparator = rootPath.EndsWith(Path.DirectorySeparatorChar)
            ? rootPath
            : rootPath + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        return candidatePath.StartsWith(rootWithSeparator, comparison);
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // A later cleanup pass can remove a temporary or orphaned file.
        }
        catch (UnauthorizedAccessException)
        {
            // A later cleanup pass can remove a temporary or orphaned file.
        }
    }

    private static PluginMediaStorageException Unavailable(Exception exception) =>
        new(PluginMediaStorageFailureKind.Unavailable, "The local media storage is unavailable.", exception);

    private sealed record DetectedImageFormat(
        MagickFormat MagickFormat,
        string Extension,
        string ContentType);
}
