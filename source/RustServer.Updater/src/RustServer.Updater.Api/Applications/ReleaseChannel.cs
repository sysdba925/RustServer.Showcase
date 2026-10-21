namespace RustServer.Updater.Api.Applications;

/// <summary>
/// Selects the GitHub Releases channel used to discover an application version.
/// </summary>
public enum ReleaseChannel
{
    Development,
    Production
}
