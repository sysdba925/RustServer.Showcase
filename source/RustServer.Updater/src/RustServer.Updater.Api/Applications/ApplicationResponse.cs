namespace RustServer.Updater.Api.Applications;

/// <summary>
/// Describes a registered application and its currently known state.
/// </summary>
public sealed record ApplicationResponse(
    string Name,
    string? InstalledVersion,
    string? AvailableVersion,
    ApplicationStatus Status)
{
    /// <summary>
    /// Gets a value indicating whether the known available version is newer than the installed version.
    /// </summary>
    public bool UpdateAvailable =>
        SemanticVersion.TryParse(InstalledVersion, out var installedVersion)
        && SemanticVersion.TryParse(AvailableVersion, out var availableVersion)
        && availableVersion!.CompareTo(installedVersion) > 0;
}

/// <summary>
/// Represents the runtime state of a registered application.
/// </summary>
public enum ApplicationStatus
{
    NotInstalled,
    Running,
    Stopped,
    Unknown
}
