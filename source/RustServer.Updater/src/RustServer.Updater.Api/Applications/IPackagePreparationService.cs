using RustServer.Updater.Api.Operations;

namespace RustServer.Updater.Api.Applications;

/// <summary>Resolves, downloads, verifies, extracts, and durably stages one release package.</summary>
public interface IPackagePreparationService
{
    /// <summary>Prepares the requested release and reports each durable operation step before it begins.</summary>
    Task<PackagePreparationResult> PrepareAsync(
        ApplicationRegistration application,
        string releaseTag,
        Func<string, CancellationToken, Task> reportStepAsync,
        CancellationToken cancellationToken);
}
