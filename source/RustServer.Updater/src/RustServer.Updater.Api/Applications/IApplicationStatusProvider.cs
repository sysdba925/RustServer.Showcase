namespace RustServer.Updater.Api.Applications;

/// <summary>
/// Provides the runtime state of an installed application.
/// </summary>
public interface IApplicationStatusProvider
{
    /// <summary>
    /// Gets the application's runtime state.
    /// </summary>
    /// <param name="application">The application whose service should be inspected.</param>
    /// <param name="isInstalled">Whether the application is currently installed.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    Task<ApplicationStatus> GetStatusAsync(
        ApplicationRegistration application,
        bool isInstalled,
        CancellationToken cancellationToken);
}
