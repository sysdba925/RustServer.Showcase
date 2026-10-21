using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using RustServer.Updater.Api.Deployments;
using RustServer.Updater.Api.Operations;

namespace RustServer.Updater.Api.Applications;

/// <summary>
/// Prepares untrusted GitHub release archives under the Updater state root.
/// It never writes to an application's installed releases directory and never changes <c>current</c>.
/// </summary>
public sealed partial class PackagePreparationService : IPackagePreparationService
{
    private static readonly JsonSerializerOptions ManifestSerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };
    private readonly IGitHubReleaseClient _gitHubReleaseClient;
    private readonly IReleaseChannelProvider _releaseChannelProvider;
    private readonly string _storagePath;
    private readonly long _maximumArchiveBytes;
    private readonly long _maximumChecksumBytes;
    private readonly long _maximumExtractedBytes;
    private readonly int _maximumArchiveEntries;
    private readonly string? _updaterApplicationName;

    /// <summary>Creates the production package preparation service.</summary>
    public PackagePreparationService(
        IGitHubReleaseClient gitHubReleaseClient,
        IReleaseChannelProvider releaseChannelProvider,
        IOptions<PackagePreparationOptions> options)
        : this(gitHubReleaseClient, releaseChannelProvider, options, registryOptions: null)
    {
    }

    /// <summary>Creates the production service and enables paired Updater bundle validation.</summary>
    public PackagePreparationService(
        IGitHubReleaseClient gitHubReleaseClient,
        IReleaseChannelProvider releaseChannelProvider,
        IOptions<PackagePreparationOptions> options,
        IOptions<ApplicationRegistryOptions>? registryOptions)
    {
        ArgumentNullException.ThrowIfNull(gitHubReleaseClient);
        ArgumentNullException.ThrowIfNull(releaseChannelProvider);
        ArgumentNullException.ThrowIfNull(options);
        var value = options.Value;
        if (string.IsNullOrWhiteSpace(value.StoragePath)
            || (!value.StoragePath.StartsWith("/", StringComparison.Ordinal) && !Path.IsPathFullyQualified(value.StoragePath)))
        {
            throw new OptionsValidationException(
                PackagePreparationOptions.SectionName,
                typeof(PackagePreparationOptions),
                [$"{PackagePreparationOptions.SectionName}:StoragePath must be absolute."]);
        }

        if (value.MaximumArchiveBytes <= 0 || value.MaximumChecksumBytes <= 0
            || value.MaximumExtractedBytes <= 0 || value.MaximumArchiveEntries <= 0)
        {
            throw new OptionsValidationException(
                PackagePreparationOptions.SectionName,
                typeof(PackagePreparationOptions),
                ["Package preparation limits must be positive."]);
        }

        _gitHubReleaseClient = gitHubReleaseClient;
        _releaseChannelProvider = releaseChannelProvider;
        _storagePath = Path.GetFullPath(value.StoragePath);
        _maximumArchiveBytes = value.MaximumArchiveBytes;
        _maximumChecksumBytes = value.MaximumChecksumBytes;
        _maximumExtractedBytes = value.MaximumExtractedBytes;
        _maximumArchiveEntries = value.MaximumArchiveEntries;
        _updaterApplicationName = registryOptions?.Value.UpdaterApplicationName?.Trim();
    }

    /// <inheritdoc />
    public async Task<PackagePreparationResult> PrepareAsync(
        ApplicationRegistration application,
        string releaseTag,
        Func<string, CancellationToken, Task> reportStepAsync,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(reportStepAsync);
        if (string.IsNullOrWhiteSpace(releaseTag))
        {
            throw new PackagePreparationException("Release tag must not be empty.");
        }

        releaseTag = releaseTag.Trim();
        var requiredEntryFile = GetRequiredEntryFile(application);
        var releaseChannel = _releaseChannelProvider.GetChannel();
        await reportStepAsync("Resolving", cancellationToken);
        ReleaseChannelPolicy.EnsureAllowedTag(releaseTag, releaseChannel);
        var release = await _gitHubReleaseClient.GetReleaseAsync(application, releaseTag, releaseChannel, cancellationToken);
        var archiveName = $"{application.Name}-{releaseTag}-linux-x64.tar.gz";
        var checksumName = $"{archiveName}.sha256";
        var archiveAsset = FindExactlyOneAsset(release, archiveName);
        var checksumAsset = FindExactlyOneAsset(release, checksumName);

        Directory.CreateDirectory(_storagePath);
        var workPath = Path.Combine(_storagePath, ".work", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workPath);
        try
        {
            await reportStepAsync("Downloading", cancellationToken);
            var archivePath = Path.Combine(workPath, "package.tar.gz");
            var checksumPath = Path.Combine(workPath, "package.tar.gz.sha256");
            await _gitHubReleaseClient.DownloadAssetAsync(
                archiveAsset.ApiUrl,
                archivePath,
                _maximumArchiveBytes,
                cancellationToken);
            await _gitHubReleaseClient.DownloadAssetAsync(
                checksumAsset.ApiUrl,
                checksumPath,
                _maximumChecksumBytes,
                cancellationToken);

            await reportStepAsync("Verifying", cancellationToken);
            var expectedChecksum = await ReadExpectedChecksumAsync(checksumPath, archiveName, cancellationToken);
            var actualChecksum = await ComputeSha256Async(archivePath, cancellationToken);
            if (!CryptographicOperations.FixedTimeEquals(
                    Convert.FromHexString(expectedChecksum),
                    Convert.FromHexString(actualChecksum)))
            {
                throw new PackagePreparationException("SHA-256 verification failed for the release archive.");
            }

            await reportStepAsync("Extracting", cancellationToken);
            var extractionPath = Path.Combine(workPath, "extracted");
            Directory.CreateDirectory(extractionPath);
            await ExtractArchiveAsync(archivePath, extractionPath, cancellationToken);
            var sourcePath = IsUpdaterBundle(application)
                ? await FindUpdaterBundleRootAsync(extractionPath, releaseTag, cancellationToken)
                : FindPackageRoot(extractionPath, requiredEntryFile);
            var preparedAt = DateTimeOffset.UtcNow;
            var contents = await PreparedPackageIntegrity.CreateAsync(sourcePath, cancellationToken);
            await WriteManifestAsync(
                sourcePath,
                new PreparedPackageManifest(application.Name, releaseTag, actualChecksum, preparedAt, contents),
                cancellationToken);
            var stagingPath = GetStagingPath(application.Name, releaseTag);
            var result = await MoveOrReuseStagingAsync(
                sourcePath,
                stagingPath,
                application.Name,
                releaseTag,
                actualChecksum,
                requiredEntryFile,
                preparedAt,
                cancellationToken);
            await reportStepAsync("Staged", cancellationToken);
            return result;
        }
        finally
        {
            DeleteWorkDirectory(workPath);
        }
    }

    private static GitHubReleaseAsset FindExactlyOneAsset(GitHubRelease release, string assetName)
    {
        var matches = release.Assets
            .Where(asset => string.Equals(asset.Name, assetName, StringComparison.Ordinal))
            .ToArray();
        return matches.Length switch
        {
            1 => matches[0],
            0 => throw new PackagePreparationException($"GitHub Release is missing required asset '{assetName}'."),
            _ => throw new PackagePreparationException($"GitHub Release contains duplicate required asset '{assetName}'.")
        };
    }

    private async Task<string> ReadExpectedChecksumAsync(string checksumPath, string archiveName, CancellationToken cancellationToken)
    {
        var matches = new List<string>();
        try
        {
            using var reader = new StreamReader(checksumPath, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            while (await reader.ReadLineAsync(cancellationToken) is { } line)
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith('#'))
                {
                    continue;
                }

                var namedMatch = NamedChecksumPattern().Match(trimmed);
                if (namedMatch.Success)
                {
                    if (string.Equals(namedMatch.Groups[2].Value, archiveName, StringComparison.Ordinal))
                    {
                        matches.Add(namedMatch.Groups[1].Value);
                    }

                    continue;
                }

                if (Sha256Pattern().IsMatch(trimmed))
                {
                    matches.Add(trimmed);
                    continue;
                }

                throw new PackagePreparationException("Checksum asset has an invalid SHA-256 format.");
            }
        }
        catch (DecoderFallbackException)
        {
            throw new PackagePreparationException("Checksum asset is not valid text.");
        }

        if (matches.Count != 1)
        {
            throw new PackagePreparationException("Checksum asset must contain exactly one SHA-256 for the release archive.");
        }

        return matches[0].ToUpperInvariant();
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81_920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var checksum = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(checksum);
    }

    private async Task ExtractArchiveAsync(string archivePath, string extractionPath, CancellationToken cancellationToken)
    {
        var seenPaths = new HashSet<string>(StringComparer.Ordinal);
        var regularFiles = new HashSet<string>(StringComparer.Ordinal);
        long extractedBytes = 0;
        var entryCount = 0;
        try
        {
            await using var archiveStream = new FileStream(
                archivePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 81_920,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            await using var gzipStream = new GZipStream(archiveStream, CompressionMode.Decompress, leaveOpen: false);
            using var tarReader = new TarReader(gzipStream, leaveOpen: false);
            TarEntry? entry;
            while ((entry = tarReader.GetNextEntry()) is not null)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var entryPath = NormalizeArchivePath(entry.Name);
                if (entryPath is null)
                {
                    continue;
                }

                if (++entryCount > _maximumArchiveEntries)
                {
                    throw new PackagePreparationException("Release archive exceeds the configured entry limit.");
                }

                if (!seenPaths.Add(entryPath) || HasRegularFileAncestor(entryPath, regularFiles))
                {
                    throw new PackagePreparationException("Release archive contains duplicate or conflicting paths.");
                }

                var destinationPath = GetExtractionDestination(extractionPath, entryPath);
                switch (entry.EntryType)
                {
                    case TarEntryType.Directory:
                        Directory.CreateDirectory(destinationPath);
                        break;
                    case TarEntryType.RegularFile:
                    case TarEntryType.V7RegularFile:
                        if (seenPaths.Any(path => path.StartsWith(entryPath + "/", StringComparison.Ordinal)))
                        {
                            throw new PackagePreparationException("Release archive contains duplicate or conflicting paths.");
                        }

                        if (entry.Length < 0 || entry.Length > _maximumExtractedBytes - extractedBytes)
                        {
                            throw new PackagePreparationException("Release archive exceeds the configured extraction size limit.");
                        }

                        extractedBytes = checked(extractedBytes + entry.Length);
                        regularFiles.Add(entryPath);
                        var parentDirectory = Path.GetDirectoryName(destinationPath)
                            ?? throw new PackagePreparationException("Release archive path is invalid.");
                        Directory.CreateDirectory(parentDirectory);
                        await CopyRegularFileAsync(entry, destinationPath, cancellationToken);
                        break;
                    default:
                        throw new PackagePreparationException("Release archive contains a link or unsupported special file.");
                }
            }
        }
        catch (PackagePreparationException)
        {
            throw;
        }
        catch (InvalidDataException)
        {
            throw new PackagePreparationException("Release archive is not a valid tar.gz archive.");
        }
        catch (EndOfStreamException)
        {
            throw new PackagePreparationException("Release archive is not a valid tar.gz archive.");
        }
        catch (IOException)
        {
            throw new PackagePreparationException("Release archive is not a valid tar archive.");
        }
    }

    private static async Task CopyRegularFileAsync(TarEntry entry, string destinationPath, CancellationToken cancellationToken)
    {
        if (entry.DataStream is null)
        {
            throw new PackagePreparationException("Release archive contains a regular file without contents.");
        }

        await using var destination = new FileStream(
            destinationPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 81_920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var buffer = new byte[81_920];
        long copiedBytes = 0;
        while (true)
        {
            var read = await entry.DataStream.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            copiedBytes = checked(copiedBytes + read);
            if (copiedBytes > entry.Length)
            {
                throw new PackagePreparationException("Release archive contains an invalid file length.");
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        if (copiedBytes != entry.Length)
        {
            throw new PackagePreparationException("Release archive is truncated.");
        }

        await destination.FlushAsync(cancellationToken);
    }

    private static string? NormalizeArchivePath(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            throw new PackagePreparationException("Release archive contains an empty path.");
        }

        if (path.StartsWith('/') || path.StartsWith('\\') || path.Contains('\\') || path.Contains("//", StringComparison.Ordinal))
        {
            throw new PackagePreparationException("Release archive contains an absolute or unsafe path.");
        }

        while (path.StartsWith("./", StringComparison.Ordinal))
        {
            path = path[2..];
        }

        path = path.TrimEnd('/');
        if (path.Length == 0)
        {
            return null;
        }

        var segments = path.Split('/');
        if (segments.Any(segment => string.IsNullOrEmpty(segment) || segment is "." or ".."))
        {
            throw new PackagePreparationException("Release archive contains a path traversal entry.");
        }

        return string.Join('/', segments);
    }

    private static bool HasRegularFileAncestor(string entryPath, HashSet<string> regularFiles)
    {
        var separatorIndex = entryPath.IndexOf('/');
        while (separatorIndex >= 0)
        {
            if (regularFiles.Contains(entryPath[..separatorIndex]))
            {
                return true;
            }

            separatorIndex = entryPath.IndexOf('/', separatorIndex + 1);
        }

        return false;
    }

    private static string GetExtractionDestination(string extractionPath, string entryPath)
    {
        var root = Path.GetFullPath(extractionPath);
        var destination = Path.GetFullPath(Path.Combine(root, entryPath.Replace('/', Path.DirectorySeparatorChar)));
        if (!destination.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new PackagePreparationException("Release archive contains a path outside the staging directory.");
        }

        return destination;
    }

    private static string GetRequiredEntryFile(ApplicationRegistration application) => application.Type switch
    {
        ApplicationType.DotNet when !string.IsNullOrWhiteSpace(application.Entrypoint) => application.Entrypoint,
        ApplicationType.StaticWeb when !string.IsNullOrWhiteSpace(application.EntryFile) => application.EntryFile,
        ApplicationType.DotNet => throw new PackagePreparationException("DotNet registration is missing its required entrypoint."),
        ApplicationType.StaticWeb => throw new PackagePreparationException("StaticWeb registration is missing its required entry file."),
        _ => throw new PackagePreparationException("Application type is not supported for package preparation.")
    };

    private static string FindPackageRoot(string extractionPath, string requiredEntryFile)
    {
        if (IsRegularFile(Path.Combine(extractionPath, requiredEntryFile)))
        {
            return extractionPath;
        }

        var topLevelEntries = Directory.EnumerateFileSystemEntries(extractionPath).ToArray();
        if (topLevelEntries.Length == 1
            && IsOrdinaryDirectory(topLevelEntries[0])
            && IsRegularFile(Path.Combine(topLevelEntries[0], requiredEntryFile)))
        {
            return topLevelEntries[0];
        }

        throw new PackagePreparationException($"Release archive does not contain required entry file '{requiredEntryFile}'.");
    }

    private static async Task<string> FindUpdaterBundleRootAsync(
        string extractionPath,
        string releaseTag,
        CancellationToken cancellationToken)
    {
        if (await IsUpdaterBundleRootAsync(extractionPath, releaseTag, cancellationToken))
        {
            return extractionPath;
        }

        var topLevelEntries = Directory.EnumerateFileSystemEntries(extractionPath).ToArray();
        if (topLevelEntries.Length == 1
            && IsOrdinaryDirectory(topLevelEntries[0])
            && await IsUpdaterBundleRootAsync(topLevelEntries[0], releaseTag, cancellationToken))
        {
            return topLevelEntries[0];
        }

        throw new PackagePreparationException("Updater release archive does not contain one complete paired Updater and Executor bundle.");
    }

    private static async Task<bool> IsUpdaterBundleRootAsync(
        string path,
        string releaseTag,
        CancellationToken cancellationToken)
    {
        try
        {
            await UpdaterReleaseBundle.ValidateAsync(path, releaseTag, cancellationToken);
            return true;
        }
        catch (InvalidDataException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private async Task WriteManifestAsync(
        string sourcePath,
        PreparedPackageManifest manifest,
        CancellationToken cancellationToken)
    {
        var manifestPath = Path.Combine(sourcePath, PreparedPackageIntegrity.ManifestFileName);
        var temporaryPath = Path.Combine(sourcePath, $".{PreparedPackageIntegrity.ManifestFileName}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, manifest, ManifestSerializerOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, manifestPath);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private async Task<PackagePreparationResult> MoveOrReuseStagingAsync(
        string sourcePath,
        string stagingPath,
        string applicationName,
        string releaseTag,
        string checksum,
        string requiredEntryFile,
        DateTimeOffset preparedAt,
        CancellationToken cancellationToken)
    {
        var parent = Path.GetDirectoryName(stagingPath)
            ?? throw new PackagePreparationException("Staging path is invalid.");
        Directory.CreateDirectory(parent);
        if (!Directory.Exists(stagingPath))
        {
            try
            {
                Directory.Move(sourcePath, stagingPath);
                return new PackagePreparationResult(applicationName, releaseTag, stagingPath, checksum, preparedAt);
            }
            catch (IOException) when (Directory.Exists(stagingPath))
            {
                // A second process completed this exact staging path while this one was extracting.
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        var existing = await ReadPreparedPackageAsync(stagingPath, cancellationToken);
        if (existing is not null
            && string.Equals(existing.Result.ApplicationName, applicationName, StringComparison.Ordinal)
            && string.Equals(existing.Result.ReleaseTag, releaseTag, StringComparison.Ordinal)
            && string.Equals(existing.Result.VerifiedChecksum, checksum, StringComparison.OrdinalIgnoreCase)
            && IsRegularFile(Path.Combine(stagingPath, requiredEntryFile))
            && await PreparedPackageIntegrity.MatchesAsync(stagingPath, existing.Contents, cancellationToken))
        {
            return existing.Result with { StagingPath = stagingPath };
        }

        throw new PackagePreparationException("A different or invalid package is already staged for this application and release tag.");
    }

    private async Task<ExistingPreparedPackage?> ReadPreparedPackageAsync(string stagingPath, CancellationToken cancellationToken)
    {
        if (!IsOrdinaryDirectory(stagingPath))
        {
            return null;
        }

        var manifestPath = Path.Combine(stagingPath, PreparedPackageIntegrity.ManifestFileName);
        if (!IsRegularFile(manifestPath))
        {
            return null;
        }

        try
        {
            await using var stream = new FileStream(
                manifestPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var manifest = await JsonSerializer.DeserializeAsync<PreparedPackageManifest>(
                stream,
                ManifestSerializerOptions,
                cancellationToken);
            if (manifest is null || !Sha256Pattern().IsMatch(manifest.VerifiedChecksum))
            {
                return null;
            }

            return new ExistingPreparedPackage(
                new PackagePreparationResult(
                    manifest.ApplicationName,
                    manifest.ReleaseTag,
                    stagingPath,
                    manifest.VerifiedChecksum.ToUpperInvariant(),
                    manifest.PreparedAt),
                manifest.Contents);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private string GetStagingPath(string applicationName, string releaseTag)
    {
        // Registration names and channel-validated tags cannot contain path separators.
        return Path.Combine(_storagePath, applicationName, releaseTag);
    }

    private bool IsUpdaterBundle(ApplicationRegistration application) =>
        !string.IsNullOrWhiteSpace(_updaterApplicationName)
        && string.Equals(application.Name, _updaterApplicationName, StringComparison.OrdinalIgnoreCase);

    private static bool IsOrdinaryDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            return false;
        }

        return (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0;
    }

    private static bool IsRegularFile(string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        return (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0;
    }

    private static void DeleteWorkDirectory(string workPath)
    {
        try
        {
            if (Directory.Exists(workPath))
            {
                Directory.Delete(workPath, recursive: true);
            }
        }
        catch (IOException)
        {
            // A failed cleanup cannot make an unverified package eligible for deployment.
        }
        catch (UnauthorizedAccessException)
        {
            // A failed cleanup cannot make an unverified package eligible for deployment.
        }
    }

    [GeneratedRegex("^([A-Fa-f0-9]{64})[ \\t]+\\*?([^ \\t]+)$", RegexOptions.CultureInvariant)]
    private static partial Regex NamedChecksumPattern();

    [GeneratedRegex("^[A-Fa-f0-9]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex Sha256Pattern();

    private sealed record ExistingPreparedPackage(
        PackagePreparationResult Result,
        IReadOnlyList<PreparedPackageContentEntry> Contents);

}
