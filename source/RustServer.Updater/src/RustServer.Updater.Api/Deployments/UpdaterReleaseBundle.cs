using System.Text.Json;

namespace RustServer.Updater.Api.Deployments;

/// <summary>
/// Describes the immutable layout of a release which updates the Updater and its
/// privileged executor together. The current link for this application always
/// points at one complete bundle, never at either component independently.
/// </summary>
public sealed record UpdaterReleaseBundleManifest(
    int FormatVersion,
    string ReleaseVersion,
    string UpdaterEntrypoint,
    string ExecutorEntrypoint);

/// <summary>Validates the only release layout accepted for Updater self-updates.</summary>
public static class UpdaterReleaseBundle
{
    /// <summary>File written by the publishing pipeline into every Updater release.</summary>
    public const string ManifestFileName = "rustserver-updater.bundle.json";

    /// <summary>Relative API entrypoint within a bundled release.</summary>
    public const string UpdaterEntrypoint = "api/RustServer.Updater.Api.dll";

    /// <summary>Relative executor entrypoint within a bundled release.</summary>
    public const string ExecutorEntrypoint = "executor/RustServer.Updater.Executor.dll";

    /// <summary>Current, deliberately small bundle-manifest schema version.</summary>
    public const int CurrentFormatVersion = 1;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Validates a staged Updater release. A release tag is part of the signed
    /// archive identity, so the manifest may not name a different version.
    /// </summary>
    public static async Task ValidateAsync(
        string bundlePath,
        string releaseVersion,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bundlePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(releaseVersion);

        var root = Path.GetFullPath(bundlePath);
        var manifestPath = Path.Combine(root, ManifestFileName);
        if (!IsRegularFile(manifestPath))
        {
            throw new InvalidDataException("Updater release bundle manifest is missing.");
        }

        UpdaterReleaseBundleManifest? manifest;
        try
        {
            await using var stream = new FileStream(
                manifestPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            manifest = await JsonSerializer.DeserializeAsync<UpdaterReleaseBundleManifest>(
                stream,
                SerializerOptions,
                cancellationToken);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Updater release bundle manifest is invalid.", exception);
        }

        if (manifest is null
            || manifest.FormatVersion != CurrentFormatVersion
            || !string.Equals(manifest.ReleaseVersion, releaseVersion, StringComparison.Ordinal)
            || !string.Equals(manifest.UpdaterEntrypoint, UpdaterEntrypoint, StringComparison.Ordinal)
            || !string.Equals(manifest.ExecutorEntrypoint, ExecutorEntrypoint, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Updater release bundle does not declare the required paired components.");
        }

        EnsureContainedRegularFile(root, UpdaterEntrypoint);
        EnsureContainedRegularFile(root, ExecutorEntrypoint);
    }

    /// <summary>Returns a manifest ready to serialize for a specific release tag.</summary>
    public static UpdaterReleaseBundleManifest CreateManifest(string releaseVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(releaseVersion);
        return new UpdaterReleaseBundleManifest(
            CurrentFormatVersion,
            releaseVersion,
            UpdaterEntrypoint,
            ExecutorEntrypoint);
    }

    private static void EnsureContainedRegularFile(string root, string relativePath)
    {
        var path = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || !IsRegularFile(path))
        {
            throw new InvalidDataException("Updater release bundle is missing a required component.");
        }
    }

    private static bool IsRegularFile(string path) =>
        File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0;
}
