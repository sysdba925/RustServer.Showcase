using RustServer.Updater.Api.Applications;
using System.Runtime.InteropServices;

namespace RustServer.Updater.Executor;

/// <summary>Controls the <c>current</c> and <c>previous</c> release links for one installation.</summary>
internal interface IReleaseLinkManager
{
    /// <summary>Gets the release selected by <c>current</c>, if any.</summary>
    string? GetCurrentReleaseVersion(ApplicationRegistration application);

    /// <summary>Gets the release selected by <c>previous</c>, if any.</summary>
    string? GetPreviousReleaseVersion(ApplicationRegistration application);

    /// <summary>Atomically points <c>current</c> at a validated immutable release directory.</summary>
    void PointCurrentAt(ApplicationRegistration application, string releasePath);

    /// <summary>Atomically points <c>previous</c> at a validated immutable release directory.</summary>
    void PointPreviousAt(ApplicationRegistration application, string releasePath);

    /// <summary>Removes an existing <c>current</c> symbolic link.</summary>
    void RemoveCurrentLink(ApplicationRegistration application);
}

/// <summary>Production symbolic-link implementation used exclusively by the executor.</summary>
internal sealed class FileReleaseLinkManager : IReleaseLinkManager
{
    /// <inheritdoc />
    public string? GetCurrentReleaseVersion(ApplicationRegistration application) =>
        GetLinkedReleaseVersion(application, "current");

    /// <inheritdoc />
    public string? GetPreviousReleaseVersion(ApplicationRegistration application) =>
        GetLinkedReleaseVersion(application, "previous");

    /// <inheritdoc />
    public void PointCurrentAt(ApplicationRegistration application, string releasePath) =>
        PointLinkAt(application.InstallationPath, "current", releasePath);

    /// <inheritdoc />
    public void PointPreviousAt(ApplicationRegistration application, string releasePath) =>
        PointLinkAt(application.InstallationPath, "previous", releasePath);

    /// <inheritdoc />
    public void RemoveCurrentLink(ApplicationRegistration application)
    {
        var currentPath = Path.Combine(application.InstallationPath, "current");
        if (!Directory.Exists(currentPath))
        {
            return;
        }

        if ((File.GetAttributes(currentPath) & FileAttributes.ReparsePoint) == 0)
        {
            throw new DeploymentExecutionException("current must be a symbolic link.");
        }

        File.Delete(currentPath);
    }

    private static void PointLinkAt(string installationPath, string linkName, string targetPath)
    {
        EnsureOrdinaryDirectory(targetPath, "Release directory is unavailable.");
        Directory.CreateDirectory(installationPath);
        var linkPath = Path.Combine(installationPath, linkName);
        if (Directory.Exists(linkPath) && (File.GetAttributes(linkPath) & FileAttributes.ReparsePoint) == 0)
        {
            throw new DeploymentExecutionException($"{linkName} must be a symbolic link.");
        }

        var temporaryLink = Path.Combine(installationPath, $".{linkName}.{Guid.NewGuid():N}.tmp");
        try
        {
            Directory.CreateSymbolicLink(temporaryLink, targetPath);
            if (Rename(temporaryLink, linkPath) != 0)
            {
                throw new IOException(
                    $"Unable to atomically update the {linkName} symbolic link.",
                    new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError()));
            }
        }
        finally
        {
            if (Directory.Exists(temporaryLink))
            {
                File.Delete(temporaryLink);
            }
        }
    }

    private static string? GetLinkedReleaseVersion(ApplicationRegistration application, string linkName)
    {
        var linkPath = Path.Combine(application.InstallationPath, linkName);
        if (!Directory.Exists(linkPath))
        {
            return null;
        }

        var link = new DirectoryInfo(linkPath);
        if ((link.Attributes & FileAttributes.ReparsePoint) == 0)
        {
            throw new DeploymentExecutionException($"{linkName} must be a symbolic link.");
        }

        var target = link.ResolveLinkTarget(returnFinalTarget: true) as DirectoryInfo;
        if (target is null || !target.Exists)
        {
            throw new DeploymentExecutionException($"{linkName} points to an unavailable release.");
        }

        var releasesPath = Path.GetFullPath(Path.Combine(application.InstallationPath, "releases"));
        var targetPath = Path.GetFullPath(target.FullName);
        if (!targetPath.StartsWith(releasesPath + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || !IsRegularReleaseName(target.Name))
        {
            throw new DeploymentExecutionException($"{linkName} points outside the registered release storage.");
        }

        return target.Name;
    }

    private static void EnsureOrdinaryDirectory(string path, string message)
    {
        if (!Directory.Exists(path) || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new DeploymentExecutionException(message);
        }
    }

    private static bool IsRegularReleaseName(string value) =>
        value.Length <= 128
        && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_');

    [DllImport("libc", EntryPoint = "rename", SetLastError = true)]
    private static extern int Rename(string sourcePath, string destinationPath);
}
