using System.Text.Json;
using Microsoft.Extensions.Options;
using RustServer.Updater.Api.Applications;
using RustServer.Updater.Api.Operations;

namespace RustServer.Updater.Api.Deployments;

/// <summary>Finds only intact Stage 6 packages at their deterministic staging location.</summary>
public interface IPreparedPackageLocator
{
    /// <summary>Returns an exact prepared package or explains why it cannot be activated.</summary>
    Task<PackagePreparationResult> GetAsync(
        ApplicationRegistration application,
        string releaseTag,
        CancellationToken cancellationToken);
}

/// <summary>Validates the persistent Stage 6 manifest before an executor request is accepted.</summary>
public sealed class PreparedPackageLocator : IPreparedPackageLocator
{
    private static readonly JsonSerializerOptions ManifestSerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly string _storagePath;
    private readonly string? _updaterApplicationName;

    /// <summary>Creates a locator rooted at the package-preparation storage path.</summary>
    public PreparedPackageLocator(IOptions<PackagePreparationOptions> options)
        : this(options, registryOptions: null)
    {
    }

    /// <summary>Creates the production locator and validates the Updater bundle layout.</summary>
    public PreparedPackageLocator(
        IOptions<PackagePreparationOptions> options,
        IOptions<ApplicationRegistryOptions>? registryOptions)
    {
        ArgumentNullException.ThrowIfNull(options);
        var storagePath = options.Value.StoragePath;
        if (string.IsNullOrWhiteSpace(storagePath)
            || (!storagePath.StartsWith("/", StringComparison.Ordinal) && !Path.IsPathFullyQualified(storagePath)))
        {
            throw new OptionsValidationException(
                PackagePreparationOptions.SectionName,
                typeof(PackagePreparationOptions),
                [$"{PackagePreparationOptions.SectionName}:StoragePath must be absolute."]);
        }

        _storagePath = Path.GetFullPath(storagePath);
        _updaterApplicationName = registryOptions?.Value.UpdaterApplicationName?.Trim();
    }

    /// <inheritdoc />
    public async Task<PackagePreparationResult> GetAsync(
        ApplicationRegistration application,
        string releaseTag,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(application);
        if (string.IsNullOrWhiteSpace(releaseTag)
            || releaseTag.Contains('/')
            || releaseTag.Contains('\\'))
        {
            throw new PreparedPackageNotFoundException("A concrete release version is required.");
        }

        var path = Path.GetFullPath(Path.Combine(_storagePath, application.Name, releaseTag.Trim()));
        var applicationRoot = Path.GetFullPath(Path.Combine(_storagePath, application.Name));
        if (!path.StartsWith(applicationRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || !IsOrdinaryDirectory(path))
        {
            throw new PreparedPackageNotFoundException($"No verified staging package exists for '{application.Name}' version '{releaseTag.Trim()}'.");
        }

        var requiredEntryFile = IsUpdaterBundle(application)
            ? UpdaterReleaseBundle.UpdaterEntrypoint
            : application.Type switch
            {
                ApplicationType.DotNet when !string.IsNullOrWhiteSpace(application.Entrypoint) => application.Entrypoint,
                ApplicationType.StaticWeb when !string.IsNullOrWhiteSpace(application.EntryFile) => application.EntryFile,
                _ => throw new PreparedPackageNotFoundException("The registered application type has no valid required entry file.")
            };
        var entrypointPath = Path.Combine(path, requiredEntryFile);
        if (!IsRegularFile(entrypointPath))
        {
            throw new PreparedPackageNotFoundException("The verified staging package no longer contains its registered entry file.");
        }

        var manifestPath = Path.Combine(path, PreparedPackageIntegrity.ManifestFileName);
        if (!IsRegularFile(manifestPath))
        {
            throw new PreparedPackageNotFoundException("The verified staging package manifest is missing.");
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
            if (manifest is null
                || !string.Equals(manifest.ApplicationName, application.Name, StringComparison.Ordinal)
                || !string.Equals(manifest.ReleaseTag, releaseTag.Trim(), StringComparison.Ordinal)
                || manifest.VerifiedChecksum is null
                || manifest.VerifiedChecksum.Length != 64
                || !manifest.VerifiedChecksum.All(char.IsAsciiHexDigit))
            {
                throw new PreparedPackageNotFoundException("The staging package manifest does not match the requested application and version.");
            }

            if (!await PreparedPackageIntegrity.MatchesAsync(path, manifest.Contents, cancellationToken))
            {
                throw new PreparedPackageNotFoundException("The verified staging package contents no longer match its manifest.");
            }

            if (IsUpdaterBundle(application))
            {
                try
                {
                    await UpdaterReleaseBundle.ValidateAsync(path, releaseTag.Trim(), cancellationToken);
                }
                catch (InvalidDataException)
                {
                    throw new PreparedPackageNotFoundException("The verified Updater package is not a complete paired Updater and Executor bundle.");
                }
            }

            return new PackagePreparationResult(
                application.Name,
                releaseTag.Trim(),
                path,
                manifest.VerifiedChecksum.ToUpperInvariant(),
                manifest.PreparedAt);
        }
        catch (JsonException)
        {
            throw new PreparedPackageNotFoundException("The staging package manifest is invalid.");
        }
        catch (IOException)
        {
            throw new PreparedPackageNotFoundException("The staging package could not be read.");
        }
    }

    private static bool IsOrdinaryDirectory(string path) =>
        Directory.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0;

    private static bool IsRegularFile(string path) =>
        File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0;

    private bool IsUpdaterBundle(ApplicationRegistration application) =>
        !string.IsNullOrWhiteSpace(_updaterApplicationName)
        && string.Equals(application.Name, _updaterApplicationName, StringComparison.OrdinalIgnoreCase);

}
