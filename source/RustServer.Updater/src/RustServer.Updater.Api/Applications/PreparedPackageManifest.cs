using System.Security.Cryptography;

namespace RustServer.Updater.Api.Applications;

/// <summary>Durable Stage 6 evidence for an extracted release package.</summary>
public sealed record PreparedPackageManifest(
    string ApplicationName,
    string ReleaseTag,
    string VerifiedChecksum,
    DateTimeOffset PreparedAt,
    IReadOnlyList<PreparedPackageContentEntry> Contents);

/// <summary>One immutable filesystem entry recorded in a prepared package manifest.</summary>
public sealed record PreparedPackageContentEntry(
    string Path,
    string Kind,
    long Size,
    string? Sha256);

/// <summary>
/// Builds and verifies the complete, link-free contents inventory used by Stage 6.
/// The manifest itself is deliberately excluded so its atomic replacement does not
/// change package evidence.
/// </summary>
public static class PreparedPackageIntegrity
{
    /// <summary>Manifest name stored alongside every verified staging tree.</summary>
    public const string ManifestFileName = ".prepared-package.json";
    private const string DirectoryKind = "directory";
    private const string FileKind = "file";

    /// <summary>Creates a deterministic inventory for every package entry.</summary>
    public static async Task<IReadOnlyList<PreparedPackageContentEntry>> CreateAsync(
        string packagePath,
        CancellationToken cancellationToken)
    {
        if (!IsOrdinaryDirectory(packagePath))
        {
            throw new IOException("Prepared package root is missing or is not an ordinary directory.");
        }

        var root = Path.GetFullPath(packagePath);
        var entries = new List<PreparedPackageContentEntry>();
        var directories = new Stack<string>();
        directories.Push(root);
        while (directories.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = directories.Pop();
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new IOException("Prepared package contains a symbolic link or reparse point.");
                }

                var relativePath = Path.GetRelativePath(root, entry).Replace(Path.DirectorySeparatorChar, '/');
                if (!IsSafeRelativePath(relativePath))
                {
                    throw new IOException("Prepared package contains an unsafe path.");
                }

                if ((attributes & FileAttributes.Directory) != 0)
                {
                    entries.Add(new PreparedPackageContentEntry(relativePath, DirectoryKind, 0, null));
                    directories.Push(entry);
                    continue;
                }

                if (!File.Exists(entry))
                {
                    throw new IOException("Prepared package contains an unsupported filesystem entry.");
                }

                // The manifest is written only after this inventory is complete.
                if (string.Equals(relativePath, ManifestFileName, StringComparison.Ordinal))
                {
                    continue;
                }

                var fileInfo = new FileInfo(entry);
                var hash = await ComputeSha256Async(entry, cancellationToken);
                if (fileInfo.Length < 0)
                {
                    throw new IOException("Prepared package contains a file with an invalid length.");
                }

                entries.Add(new PreparedPackageContentEntry(relativePath, FileKind, fileInfo.Length, hash));
            }
        }

        return entries.OrderBy(entry => entry.Path, StringComparer.Ordinal).ToArray();
    }

    /// <summary>Checks the exact staged tree against a previously recorded inventory.</summary>
    public static async Task<bool> MatchesAsync(
        string packagePath,
        IReadOnlyList<PreparedPackageContentEntry>? expected,
        CancellationToken cancellationToken)
    {
        if (expected is null || expected.Count == 0 || !AreValid(expected))
        {
            return false;
        }

        try
        {
            var actual = await CreateAsync(packagePath, cancellationToken);
            return actual.Count == expected.Count
                && actual.SequenceEqual(expected.OrderBy(entry => entry.Path, StringComparer.Ordinal));
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static bool AreValid(IReadOnlyList<PreparedPackageContentEntry> entries)
    {
        string? previousPath = null;
        foreach (var entry in entries.OrderBy(entry => entry.Path, StringComparer.Ordinal))
        {
            if (!IsSafeRelativePath(entry.Path)
                || string.Equals(entry.Path, ManifestFileName, StringComparison.Ordinal)
                || string.Equals(entry.Path, previousPath, StringComparison.Ordinal))
            {
                return false;
            }

            if (entry.Kind == DirectoryKind && entry.Size == 0 && entry.Sha256 is null)
            {
                previousPath = entry.Path;
                continue;
            }

            if (entry.Kind == FileKind && entry.Size >= 0 && entry.Sha256 is { Length: 64 } hash
                && hash.All(char.IsAsciiHexDigit))
            {
                previousPath = entry.Path;
                continue;
            }

            return false;
        }

        return true;
    }

    private static bool IsOrdinaryDirectory(string path) =>
        Directory.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0;

    private static bool IsSafeRelativePath(string path) =>
        !string.IsNullOrWhiteSpace(path)
        && !path.StartsWith("/", StringComparison.Ordinal)
        && !path.Contains('\\')
        && path.Split('/').All(segment => segment.Length > 0 && segment is not "." and not "..");

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81_920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
    }
}
