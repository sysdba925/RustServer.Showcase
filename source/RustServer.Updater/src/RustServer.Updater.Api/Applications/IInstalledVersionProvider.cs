namespace RustServer.Updater.Api.Applications;

/// <summary>
/// Provides the version currently installed for a registered application.
/// </summary>
public interface IInstalledVersionProvider
{
    /// <summary>
    /// Gets the installed version, or <see langword="null"/> when no valid installation is present.
    /// </summary>
    /// <param name="application">The application whose installation should be inspected.</param>
    string? GetInstalledVersion(ApplicationRegistration application);
}
