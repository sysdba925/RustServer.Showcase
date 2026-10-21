using Microsoft.Extensions.Options;

namespace RustServer.Plugins.Features.PluginManagement.Runtime;

public sealed class RuntimeFiles(
    IOptions<RuntimeOptions> options,
    ManagedFileMapping mapping) : IRuntimeFiles
{
    private readonly string serverRootPath = options.Value.ServerRootPath;

    public Task<RuntimeAvailability> GetAvailabilityAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(GetAvailability());
    }

    public Task<IReadOnlyList<ManagedFile>> ListAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var runtimeRootPath = GetAvailableRuntimeRootPath();
        var files = new List<ManagedFile>();

        CollectDirectoryFiles(runtimeRootPath, "plugins", recursive: false, files);
        CollectDirectoryFiles(runtimeRootPath, "config", recursive: true, files);
        CollectDirectoryFiles(runtimeRootPath, "data", recursive: true, files);
        CollectDirectoryFiles(runtimeRootPath, "lang", recursive: true, files);
        CollectDirectoryFiles(runtimeRootPath, "discord", recursive: true, files);
        CollectRootFile(runtimeRootPath, "oxide.config.json", files);
        CollectRootFile(runtimeRootPath, "discord.config.json", files);

        return Task.FromResult<IReadOnlyList<ManagedFile>>(
            files.OrderBy(file => file.RuntimeRelativePath, StringComparer.Ordinal).ToArray());
    }

    public async Task<byte[]> ReadAsync(ManagedFile file, CancellationToken cancellationToken)
    {
        var filePath = GetRecognizedFilePath(file);
        EnsureExistingFileIsSafe(filePath);

        return await File.ReadAllBytesAsync(filePath, cancellationToken);
    }

    public async Task WriteAsync(
        ManagedFile file,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken)
    {
        var filePath = GetRecognizedFilePath(file);
        var parentDirectoryPath = Path.GetDirectoryName(filePath) ??
            throw new RuntimeFilesException(
                RuntimeAvailabilityStatus.UnsafePath,
                "Managed file path does not have a parent directory.");
        EnsureSafeParentDirectory(parentDirectoryPath);
        EnsureTargetIsNotUnsafe(filePath);

        var temporaryFilePath = Path.Combine(
            parentDirectoryPath,
            $".{Path.GetFileName(filePath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            await File.WriteAllBytesAsync(temporaryFilePath, content, cancellationToken);
            EnsureSafeParentDirectory(parentDirectoryPath);
            EnsureTargetIsNotUnsafe(filePath);
            File.Move(temporaryFilePath, filePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryFilePath))
            {
                File.Delete(temporaryFilePath);
            }
        }
    }

    public Task DeleteAsync(ManagedFile file, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var filePath = GetRecognizedFilePath(file);
        EnsureTargetIsNotUnsafe(filePath);

        if (TryGetAttributes(filePath) is not null)
        {
            File.Delete(filePath);
        }

        return Task.CompletedTask;
    }

    private RuntimeAvailability GetAvailability()
    {
        try
        {
            return GetAvailabilityCore();
        }
        catch (RuntimeFilesException)
        {
            return new RuntimeAvailability(RuntimeAvailabilityStatus.UnsafePath);
        }
        catch (UnauthorizedAccessException)
        {
            return new RuntimeAvailability(RuntimeAvailabilityStatus.UnsafePath);
        }
    }

    private RuntimeAvailability GetAvailabilityCore()
    {
        if (string.IsNullOrWhiteSpace(serverRootPath))
        {
            return new RuntimeAvailability(RuntimeAvailabilityStatus.ServerRootMissing);
        }

        string fullServerRootPath;
        try
        {
            fullServerRootPath = Path.GetFullPath(serverRootPath);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            return new RuntimeAvailability(RuntimeAvailabilityStatus.UnsafePath);
        }

        if (!Directory.Exists(fullServerRootPath))
        {
            return new RuntimeAvailability(RuntimeAvailabilityStatus.ServerRootMissing);
        }

        if (ContainsReparsePoint(fullServerRootPath))
        {
            return new RuntimeAvailability(RuntimeAvailabilityStatus.UnsafePath);
        }

        var runtimeRootPath = Path.Combine(fullServerRootPath, ManagedFileMapping.ManagedRootDirectoryName);
        if (!Directory.Exists(runtimeRootPath))
        {
            return new RuntimeAvailability(RuntimeAvailabilityStatus.ManagedRootMissing);
        }

        return IsReparsePoint(runtimeRootPath)
            ? new RuntimeAvailability(RuntimeAvailabilityStatus.UnsafePath)
            : new RuntimeAvailability(RuntimeAvailabilityStatus.Available);
    }

    private string GetAvailableRuntimeRootPath()
    {
        var availability = GetAvailability();
        if (!availability.IsAvailable)
        {
            throw new RuntimeFilesException(
                availability.Status,
                "Managed Runtime files are unavailable.");
        }

        return Path.Combine(Path.GetFullPath(serverRootPath), ManagedFileMapping.ManagedRootDirectoryName);
    }

    private string GetRecognizedFilePath(ManagedFile file)
    {
        ArgumentNullException.ThrowIfNull(file);

        var recognizedFile = mapping.FromRepositoryPath(file.Repository, file.RepositoryRelativePath);
        if (recognizedFile is null ||
            !string.Equals(recognizedFile.RuntimeRelativePath, file.RuntimeRelativePath, StringComparison.Ordinal))
        {
            throw new RuntimeFilesException(
                RuntimeAvailabilityStatus.UnsafePath,
                "The managed file mapping is not recognized.");
        }

        var runtimeRootPath = GetAvailableRuntimeRootPath();
        var pathWithinRuntimeRoot = file.RuntimeRelativePath[
            (ManagedFileMapping.ManagedRootDirectoryName.Length + 1)..];
        var filePath = CombineLogicalPath(runtimeRootPath, pathWithinRuntimeRoot);
        EnsurePathIsWithinRuntimeRoot(runtimeRootPath, filePath);

        return filePath;
    }

    private void CollectDirectoryFiles(
        string runtimeRootPath,
        string directoryName,
        bool recursive,
        ICollection<ManagedFile> files)
    {
        var directoryPath = Path.Combine(runtimeRootPath, directoryName);
        if (!Directory.Exists(directoryPath))
        {
            return;
        }

        EnsureExistingPathIsSafe(directoryPath);
        CollectDirectoryFilesCore(directoryPath, directoryName, recursive, files);
    }

    private void CollectDirectoryFilesCore(
        string directoryPath,
        string runtimeRelativeDirectory,
        bool recursive,
        ICollection<ManagedFile> files)
    {
        foreach (var entryPath in Directory.EnumerateFileSystemEntries(directoryPath))
        {
            var attributes = GetAttributesOrThrow(entryPath);
            if (attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                throw new RuntimeFilesException(
                    RuntimeAvailabilityStatus.UnsafePath,
                    "A managed Runtime path contains a symlink or reparse point.");
            }

            var entryName = Path.GetFileName(entryPath);
            var runtimeRelativePath = $"{runtimeRelativeDirectory}/{entryName}";

            if (attributes.HasFlag(FileAttributes.Directory))
            {
                if (recursive)
                {
                    CollectDirectoryFilesCore(entryPath, runtimeRelativePath, recursive: true, files);
                }

                continue;
            }

            var managedFile = mapping.FromRuntimePath(
                $"{ManagedFileMapping.ManagedRootDirectoryName}/{runtimeRelativePath}");
            if (managedFile is not null)
            {
                files.Add(managedFile);
            }
        }
    }

    private void CollectRootFile(string runtimeRootPath, string fileName, ICollection<ManagedFile> files)
    {
        var filePath = Path.Combine(runtimeRootPath, fileName);
        if (TryGetAttributes(filePath) is null)
        {
            return;
        }

        EnsureExistingFileIsSafe(filePath);
        var managedFile = mapping.FromRuntimePath($"{ManagedFileMapping.ManagedRootDirectoryName}/{fileName}");
        if (managedFile is not null)
        {
            files.Add(managedFile);
        }
    }

    private void EnsureExistingFileIsSafe(string filePath)
    {
        EnsureTargetIsNotUnsafe(filePath);
        if (TryGetAttributes(filePath) is null)
        {
            throw new FileNotFoundException("The managed Runtime file does not exist.", filePath);
        }
    }

    private void EnsureTargetIsNotUnsafe(string filePath)
    {
        var runtimeRootPath = GetAvailableRuntimeRootPath();
        EnsurePathIsWithinRuntimeRoot(runtimeRootPath, filePath);

        var parentDirectoryPath = Path.GetDirectoryName(filePath) ??
            throw new RuntimeFilesException(
                RuntimeAvailabilityStatus.UnsafePath,
                "Managed file path does not have a parent directory.");
        EnsureExistingPathChainIsSafe(runtimeRootPath, parentDirectoryPath);

        var attributes = TryGetAttributes(filePath);
        if (attributes is null)
        {
            return;
        }

        if (attributes.Value.HasFlag(FileAttributes.ReparsePoint) ||
            attributes.Value.HasFlag(FileAttributes.Directory))
        {
            throw new RuntimeFilesException(
                RuntimeAvailabilityStatus.UnsafePath,
                "A managed Runtime file is not a safe regular file.");
        }
    }

    private void EnsureSafeParentDirectory(string directoryPath)
    {
        var runtimeRootPath = GetAvailableRuntimeRootPath();
        EnsurePathIsWithinRuntimeRoot(runtimeRootPath, directoryPath);

        var relativePath = Path.GetRelativePath(runtimeRootPath, directoryPath);
        var currentPath = runtimeRootPath;
        foreach (var segment in relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            if (string.IsNullOrEmpty(segment))
            {
                continue;
            }

            currentPath = Path.Combine(currentPath, segment);
            if (!Directory.Exists(currentPath))
            {
                Directory.CreateDirectory(currentPath);
            }

            EnsureExistingPathIsSafe(currentPath);
        }
    }

    private static string CombineLogicalPath(string rootPath, string logicalPath)
    {
        var segments = logicalPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Aggregate(rootPath, Path.Combine);
    }

    private static void EnsurePathIsWithinRuntimeRoot(string runtimeRootPath, string path)
    {
        var relativePath = Path.GetRelativePath(runtimeRootPath, path);
        if (Path.IsPathRooted(relativePath) ||
            string.Equals(relativePath, "..", StringComparison.Ordinal) ||
            relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
            relativePath.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal))
        {
            throw new RuntimeFilesException(
                RuntimeAvailabilityStatus.UnsafePath,
                "Managed file path is outside the Runtime root.");
        }
    }

    private static void EnsureExistingPathIsSafe(string path)
    {
        if (IsReparsePoint(path))
        {
            throw new RuntimeFilesException(
                RuntimeAvailabilityStatus.UnsafePath,
                "A managed Runtime path contains a symlink or reparse point.");
        }
    }

    private static bool IsReparsePoint(string path) =>
        GetAttributesOrThrow(path).HasFlag(FileAttributes.ReparsePoint);

    private static bool ContainsReparsePoint(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var rootPath = Path.GetPathRoot(fullPath) ?? string.Empty;
        var currentPath = rootPath;
        var relativePath = fullPath[rootPath.Length..];

        foreach (var segment in relativePath.Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            currentPath = Path.Combine(currentPath, segment);
            if (IsReparsePoint(currentPath))
            {
                return true;
            }
        }

        return false;
    }

    private static void EnsureExistingPathChainIsSafe(string runtimeRootPath, string path)
    {
        var relativePath = Path.GetRelativePath(runtimeRootPath, path);
        var currentPath = runtimeRootPath;

        foreach (var segment in relativePath.Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            currentPath = Path.Combine(currentPath, segment);
            if (TryGetAttributes(currentPath) is null)
            {
                return;
            }

            EnsureExistingPathIsSafe(currentPath);
        }
    }

    private static FileAttributes GetAttributesOrThrow(string path)
    {
        try
        {
            return File.GetAttributes(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new RuntimeFilesException(
                RuntimeAvailabilityStatus.UnsafePath,
                "Runtime path attributes could not be read safely.",
                exception);
        }
    }

    private static FileAttributes? TryGetAttributes(string path)
    {
        try
        {
            return File.GetAttributes(path);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new RuntimeFilesException(
                RuntimeAvailabilityStatus.UnsafePath,
                "Runtime path attributes could not be read safely.",
                exception);
        }
    }
}
