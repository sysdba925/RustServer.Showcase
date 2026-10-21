using System.Security;

namespace RustServer.Updater.Api.Applications;

/// <summary>
/// Reads an installed application's version from its <c>current</c> symbolic link.
/// </summary>
public sealed class FileInstalledVersionProvider : IInstalledVersionProvider
{
    private const string CurrentLinkName = "current";

    /// <inheritdoc />
    public string? GetInstalledVersion(ApplicationRegistration application)
    {
        var installationPath = application.InstallationPath;
        if (string.IsNullOrWhiteSpace(installationPath))
        {
            return null;
        }

        try
        {
            var currentLink = new DirectoryInfo(Path.Combine(installationPath, CurrentLinkName));
            if (!currentLink.Exists)
            {
                return null;
            }

            var releaseDirectory = currentLink.ResolveLinkTarget(returnFinalTarget: true);
            return releaseDirectory is DirectoryInfo directory && directory.Exists
                ? directory.Name
                : null;
        }
        catch (Exception exception) when (exception is IOException
                                         or UnauthorizedAccessException
                                         or SecurityException
                                         or ArgumentException
                                         or NotSupportedException)
        {
            return null;
        }
    }
}
