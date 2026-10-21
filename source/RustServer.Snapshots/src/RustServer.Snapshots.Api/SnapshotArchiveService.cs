using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;

public sealed class SnapshotArchiveService
{
    private static readonly JsonSerializerOptions MetadataSerializerOptions = CreateMetadataSerializerOptions();
    private readonly SnapshotOptions _options;
    private readonly TimeProvider _timeProvider;

    public SnapshotArchiveService(SnapshotOptions options, TimeProvider timeProvider)
    {
        _options = options;
        _timeProvider = timeProvider;
    }

    public void EnsureCreateReady()
    {
        var paths = GetValidatedPaths(requireSources: true);
        EnsureStorageDirectory(paths.StoragePath);
        EnsureSufficientFreeSpace(paths.StoragePath);
    }

    public void EnsureRestoreReady()
    {
        var paths = GetValidatedPaths(requireSources: false);
        EnsureStorageDirectory(paths.StoragePath);
    }

    public IReadOnlyList<SnapshotMetadata> GetSnapshots()
    {
        var paths = GetValidatedPaths(requireSources: false);
        EnsureStorageDirectory(paths.StoragePath);

        return Directory.EnumerateFiles(paths.StoragePath, "*.json", SearchOption.TopDirectoryOnly)
            .Select(ReadMetadata)
            .Where(metadata => File.Exists(GetArchivePath(paths, metadata.Id)))
            .OrderByDescending(metadata => metadata.CreatedAt)
            .ToArray();
    }

    public SnapshotMetadata? GetSnapshot(string id)
    {
        if (!IsSafeId(id))
        {
            return null;
        }

        var paths = GetValidatedPaths(requireSources: false);
        EnsureStorageDirectory(paths.StoragePath);
        var metadataPath = GetMetadataPath(paths, id);
        if (!File.Exists(metadataPath))
        {
            return null;
        }

        var metadata = ReadMetadata(metadataPath);
        return File.Exists(GetArchivePath(paths, metadata.Id)) ? metadata : null;
    }

    public bool DeleteSnapshot(string id)
    {
        if (!IsSafeId(id))
        {
            return false;
        }

        var paths = GetValidatedPaths(requireSources: false);
        EnsureStorageDirectory(paths.StoragePath);
        var metadataPath = GetMetadataPath(paths, id);
        if (!File.Exists(metadataPath))
        {
            return false;
        }

        var metadata = ReadMetadata(metadataPath);
        File.Delete(GetArchivePath(paths, metadata.Id));
        File.Delete(metadataPath);
        return true;
    }

    public SnapshotMetadata CreateSnapshot(SnapshotType type)
    {
        EnsureCreateReady();
        var paths = GetValidatedPaths(requireSources: true);
        var id = Guid.NewGuid().ToString("N");
        var archivePath = GetArchivePath(paths, id);
        var temporaryArchivePath = archivePath + ".partial";
        var metadataPath = GetMetadataPath(paths, id);

        try
        {
            CreateArchive(temporaryArchivePath, paths);
            var metadata = new SnapshotMetadata(
                id,
                _timeProvider.GetUtcNow(),
                type,
                new FileInfo(temporaryArchivePath).Length);

            File.Move(temporaryArchivePath, archivePath);
            WriteMetadata(metadataPath, metadata);
            ApplyRetention(paths);
            return metadata;
        }
        catch
        {
            File.Delete(temporaryArchivePath);
            if (!File.Exists(metadataPath))
            {
                File.Delete(archivePath);
            }

            throw;
        }
    }

    public void RestoreSnapshot(SnapshotMetadata snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        EnsureRestoreReady();
        var paths = GetValidatedPaths(requireSources: false);
        var archivePath = GetArchivePath(paths, snapshot.Id);
        if (!File.Exists(archivePath))
        {
            throw new SnapshotStorageException("The requested snapshot archive is unavailable.");
        }

        var stagingPath = Path.Combine(paths.StoragePath, ".restore-" + snapshot.Id + "-" + Guid.NewGuid().ToString("N"));
        try
        {
            ExtractArchive(archivePath, stagingPath);
            ReplaceDirectoryContents(Path.Combine(stagingPath, "server"), paths.ServerIdentityPath);
            ReplaceDirectoryContents(Path.Combine(stagingPath, "oxide", "data"), paths.OxideDataPath);
            ReplaceDirectoryContents(Path.Combine(stagingPath, "oxide", "config"), paths.OxideConfigPath);
            ReplaceDirectoryContents(Path.Combine(stagingPath, "oxide", "plugins"), paths.OxidePluginsPath);
            ReplaceDirectoryContents(Path.Combine(stagingPath, "oxide", "lang"), paths.OxideLangPath);
        }
        finally
        {
            if (Directory.Exists(stagingPath))
            {
                Directory.Delete(stagingPath, recursive: true);
            }
        }
    }

    private void ApplyRetention(SnapshotPaths paths)
    {
        foreach (var metadata in GetSnapshots().Skip(_options.RetentionCount))
        {
            File.Delete(GetArchivePath(paths, metadata.Id));
            File.Delete(GetMetadataPath(paths, metadata.Id));
        }
    }

    private static void CreateArchive(string archivePath, SnapshotPaths paths)
    {
        using var stream = new FileStream(archivePath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false);

        AddDirectory(archive, paths.ServerIdentityPath, "server");
        AddDirectory(archive, paths.OxideDataPath, "oxide/data");
        AddDirectory(archive, paths.OxideConfigPath, "oxide/config");
        AddDirectory(archive, paths.OxidePluginsPath, "oxide/plugins");
        AddDirectory(archive, paths.OxideLangPath, "oxide/lang");
    }

    private static void AddDirectory(ZipArchive archive, string sourceDirectory, string archiveDirectory)
    {
        archive.CreateEntry(archiveDirectory + "/", CompressionLevel.NoCompression);

        foreach (var directory in Directory.EnumerateDirectories(sourceDirectory))
        {
            var directoryInfo = new DirectoryInfo(directory);
            if ((directoryInfo.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                continue;
            }

            AddDirectory(archive, directory, archiveDirectory + "/" + directoryInfo.Name);
        }

        foreach (var file in Directory.EnumerateFiles(sourceDirectory))
        {
            var fileInfo = new FileInfo(file);
            if ((fileInfo.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                continue;
            }

            var entry = archive.CreateEntry(archiveDirectory + "/" + fileInfo.Name, CompressionLevel.Optimal);
            using var source = fileInfo.OpenRead();
            using var destination = entry.Open();
            source.CopyTo(destination);
        }
    }

    private static void ExtractArchive(string archivePath, string stagingPath)
    {
        Directory.CreateDirectory(stagingPath);

        using var stream = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
        foreach (var entry in archive.Entries)
        {
            var destination = GetStagingDestination(stagingPath, entry.FullName);
            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(destination);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            using var source = entry.Open();
            using var target = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None);
            source.CopyTo(target);
        }

        foreach (var requiredDirectory in new[]
                 {
                     Path.Combine(stagingPath, "server"),
                     Path.Combine(stagingPath, "oxide", "data"),
                     Path.Combine(stagingPath, "oxide", "config"),
                     Path.Combine(stagingPath, "oxide", "plugins"),
                     Path.Combine(stagingPath, "oxide", "lang")
                 })
        {
            if (!Directory.Exists(requiredDirectory))
            {
                throw new SnapshotStorageException("The snapshot archive is incomplete.");
            }
        }
    }

    private static string GetStagingDestination(string stagingPath, string entryName)
    {
        if (entryName.Contains('\\', StringComparison.Ordinal) || entryName.StartsWith("/", StringComparison.Ordinal))
        {
            throw new SnapshotStorageException("The snapshot archive contains an unsafe path.");
        }

        var mappings = new[]
        {
            (ArchiveRoot: "oxide/plugins", StagingRoot: Path.Combine(stagingPath, "oxide", "plugins")),
            (ArchiveRoot: "oxide/config", StagingRoot: Path.Combine(stagingPath, "oxide", "config")),
            (ArchiveRoot: "oxide/data", StagingRoot: Path.Combine(stagingPath, "oxide", "data")),
            (ArchiveRoot: "oxide/lang", StagingRoot: Path.Combine(stagingPath, "oxide", "lang")),
            (ArchiveRoot: "server", StagingRoot: Path.Combine(stagingPath, "server"))
        };

        foreach (var (archiveRoot, stagingRoot) in mappings)
        {
            if (entryName.Equals(archiveRoot, StringComparison.Ordinal) ||
                entryName.Equals(archiveRoot + "/", StringComparison.Ordinal))
            {
                return stagingRoot;
            }

            var prefix = archiveRoot + "/";
            if (!entryName.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            var relativePath = entryName[prefix.Length..];
            var segments = relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Any(segment => segment is "." or ".."))
            {
                throw new SnapshotStorageException("The snapshot archive contains an unsafe path.");
            }

            var destination = Path.GetFullPath(Path.Combine(stagingRoot, Path.Combine(segments)));
            if (!IsSameOrDescendant(destination, stagingRoot))
            {
                throw new SnapshotStorageException("The snapshot archive contains an unsafe path.");
            }

            return destination;
        }

        throw new SnapshotStorageException("The snapshot archive contains an unsupported path.");
    }

    private static void ReplaceDirectoryContents(string sourceDirectory, string destinationDirectory)
    {
        if (Directory.Exists(destinationDirectory))
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(destinationDirectory))
            {
                if (Directory.Exists(entry))
                {
                    Directory.Delete(entry, recursive: true);
                }
                else
                {
                    File.Delete(entry);
                }
            }
        }
        else
        {
            Directory.CreateDirectory(destinationDirectory);
        }

        CopyDirectoryContents(sourceDirectory, destinationDirectory);
    }

    private static void CopyDirectoryContents(string sourceDirectory, string destinationDirectory)
    {
        foreach (var directory in Directory.EnumerateDirectories(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDirectory, directory);
            Directory.CreateDirectory(Path.Combine(destinationDirectory, relativePath));
        }

        foreach (var file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDirectory, file);
            var destination = Path.Combine(destinationDirectory, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, overwrite: true);
        }
    }

    private SnapshotPaths GetValidatedPaths(bool requireSources)
    {
        if (_options.RetentionCount < 1)
        {
            throw new SnapshotConfigurationException("Snapshots:RetentionCount must be at least 1.");
        }

        if (_options.MinimumFreeSpaceBytes < 0)
        {
            throw new SnapshotConfigurationException("Snapshots:MinimumFreeSpaceBytes cannot be negative.");
        }

        var paths = new SnapshotPaths(
            GetFullPath("Snapshots:ServerIdentityPath", _options.ServerIdentityPath),
            GetFullPath("Snapshots:OxideDataPath", _options.OxideDataPath),
            GetFullPath("Snapshots:OxideConfigPath", _options.OxideConfigPath),
            GetFullPath("Snapshots:OxidePluginsPath", _options.OxidePluginsPath),
            GetFullPath("Snapshots:OxideLangPath", _options.OxideLangPath),
            GetFullPath("Snapshots:StoragePath", _options.StoragePath));

        var allPaths = new[]
        {
            paths.ServerIdentityPath,
            paths.OxideDataPath,
            paths.OxideConfigPath,
            paths.OxidePluginsPath,
            paths.OxideLangPath,
            paths.StoragePath
        };
        for (var first = 0; first < allPaths.Length; first++)
        {
            for (var second = first + 1; second < allPaths.Length; second++)
            {
                if (IsSameOrDescendant(allPaths[first], allPaths[second]) ||
                    IsSameOrDescendant(allPaths[second], allPaths[first]))
                {
                    throw new SnapshotConfigurationException("Snapshot source and storage paths must be distinct and non-overlapping.");
                }
            }
        }

        if (requireSources)
        {
            foreach (var sourcePath in allPaths[..^1])
            {
                if (!Directory.Exists(sourcePath))
                {
                    throw new SnapshotConfigurationException("A configured snapshot source directory does not exist.");
                }
            }
        }

        return paths;
    }

    private static string GetFullPath(string configurationKey, string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
        {
            throw new SnapshotConfigurationException("Configuration value '" + configurationKey + "' must be an absolute path.");
        }

        return Path.GetFullPath(path);
    }

    private void EnsureSufficientFreeSpace(string storagePath)
    {
        if (_options.MinimumFreeSpaceBytes == 0)
        {
            return;
        }

        try
        {
            var root = Path.GetPathRoot(storagePath)
                ?? throw new SnapshotStorageException("Available snapshot storage space could not be determined.");
            var availableSpace = new DriveInfo(root).AvailableFreeSpace;
            if (availableSpace < _options.MinimumFreeSpaceBytes)
            {
                throw new SnapshotStorageException("Insufficient free space is available for a new snapshot.");
            }
        }
        catch (SnapshotStorageException)
        {
            throw;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
        {
            throw new SnapshotStorageException("Available snapshot storage space could not be determined.", exception);
        }
    }

    private static void EnsureStorageDirectory(string storagePath)
    {
        try
        {
            Directory.CreateDirectory(storagePath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new SnapshotStorageException("Snapshot storage is unavailable.", exception);
        }
    }

    private static SnapshotMetadata ReadMetadata(string metadataPath)
    {
        try
        {
            var metadata = JsonSerializer.Deserialize<SnapshotMetadata>(File.ReadAllText(metadataPath), MetadataSerializerOptions);
            if (metadata is null || !IsSafeId(metadata.Id) || metadata.SizeBytes < 0)
            {
                throw new SnapshotStorageException("Snapshot metadata is invalid.");
            }

            return metadata;
        }
        catch (SnapshotStorageException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            throw new SnapshotStorageException("Snapshot metadata is unreadable.", exception);
        }
    }

    private static void WriteMetadata(string metadataPath, SnapshotMetadata metadata)
    {
        var temporaryPath = metadataPath + ".partial";
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(metadata, MetadataSerializerOptions));
            File.Move(temporaryPath, metadataPath);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    private static string GetArchivePath(SnapshotPaths paths, string id) => Path.Combine(paths.StoragePath, id + ".zip");

    private static string GetMetadataPath(SnapshotPaths paths, string id) => Path.Combine(paths.StoragePath, id + ".json");

    private static bool IsSafeId(string id) =>
        id.Length == 32 && id.All(character => char.IsAsciiHexDigit(character));

    private static bool IsSameOrDescendant(string path, string potentialParent)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var normalizedPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var normalizedParent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(potentialParent));
        return normalizedPath.Equals(normalizedParent, comparison) ||
               normalizedPath.StartsWith(normalizedParent + Path.DirectorySeparatorChar, comparison) ||
               normalizedPath.StartsWith(normalizedParent + Path.AltDirectorySeparatorChar, comparison);
    }

    private static JsonSerializerOptions CreateMetadataSerializerOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        return options;
    }

    private sealed record SnapshotPaths(
        string ServerIdentityPath,
        string OxideDataPath,
        string OxideConfigPath,
        string OxidePluginsPath,
        string OxideLangPath,
        string StoragePath);
}

public sealed record SnapshotMetadata(
    string Id,
    DateTimeOffset CreatedAt,
    SnapshotType Type,
    long SizeBytes);

public sealed class SnapshotConfigurationException : Exception
{
    public SnapshotConfigurationException(string message)
        : base(message)
    {
    }
}

public sealed class SnapshotStorageException : Exception
{
    public SnapshotStorageException(string message)
        : base(message)
    {
    }

    public SnapshotStorageException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
