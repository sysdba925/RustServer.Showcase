using RustServer.Updater.Api.Applications;

namespace RustServer.Updater.Executor;

/// <summary>
/// Installs the one fixed root-owned NetworkAccess helper unit bundled with
/// RustServer.Control. The helper is not a separately versioned application.
/// Bootstrap only prepares nftables and the non-root Control account.
/// </summary>
internal sealed class ControlNetworkAccessUnitInstaller(
    string systemdUnitDirectory,
    ISystemdController systemd)
{
    internal const string UnitName = "rustserver-network-access-helper.service";
    private const string ControlName = "RustServer.Control";
    private const string ControlServiceName = "rustserver-control.service";
    private const string DockerDropInName = "rustserver-network-access.conf";
    private const string DockerDropInContents = "[Service]\nExecStartPre=/usr/sbin/nft -f /opt/rustserver/control/current/network-access/nftables/rustserver-network-access-guard.nft\n";
    private readonly string _systemdUnitDirectory = systemdUnitDirectory;
    private readonly ISystemdController _systemd = systemd;

    internal static bool OwnsHelper(ApplicationRegistration application) =>
        string.Equals(application.Name, ControlName, StringComparison.Ordinal) &&
        string.Equals(application.ServiceName, ControlServiceName, StringComparison.Ordinal);

    internal static void ValidateRelease(string releasePath)
    {
        var sourcePath = Path.Combine(releasePath, "network-access", "systemd", UnitName);
        if (!File.Exists(sourcePath) || (File.GetAttributes(sourcePath) & FileAttributes.ReparsePoint) != 0)
        {
            throw new DeploymentExecutionException("The Control release is missing its reviewed NetworkAccess helper unit.");
        }
        var guard = Path.Combine(releasePath, "network-access", "nftables", "rustserver-network-access-guard.nft");
        if (!File.Exists(guard) || (File.GetAttributes(guard) & FileAttributes.ReparsePoint) != 0)
        {
            throw new DeploymentExecutionException("The Control release is missing its NetworkAccess firewall guard.");
        }
    }

    internal bool IsInstalled(ApplicationRegistration application)
    {
        var link = new FileInfo(Path.Combine(_systemdUnitDirectory, UnitName)).LinkTarget;
        return link is not null && string.Equals(link, GetExpectedTarget(application), StringComparison.Ordinal);
    }

    internal async Task InstallAsync(
        ApplicationRegistration application,
        string releasePath,
        CancellationToken cancellationToken)
    {
        ValidateRelease(releasePath);
        var unitPath = Path.Combine(_systemdUnitDirectory, UnitName);
        var target = GetExpectedTarget(application);
        Directory.CreateDirectory(_systemdUnitDirectory);

        var existing = new FileInfo(unitPath);
        if (existing.LinkTarget is not null)
        {
            if (!string.Equals(existing.LinkTarget, target, StringComparison.Ordinal))
            {
                throw new DeploymentExecutionException("The existing NetworkAccess helper systemd unit points to an unexpected release.");
            }
        }
        else
        {
            if (File.Exists(unitPath) || Directory.Exists(unitPath))
            {
                throw new DeploymentExecutionException("The NetworkAccess helper systemd unit is not a managed link.");
            }
            var temporaryLink = Path.Combine(_systemdUnitDirectory, "." + UnitName + "." + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                File.CreateSymbolicLink(temporaryLink, target);
                File.Move(temporaryLink, unitPath);
            }
            finally
            {
                if (new FileInfo(temporaryLink).LinkTarget is not null)
                {
                    File.Delete(temporaryLink);
                }
            }
        }

        // Docker's own ExecStartPre must install the guard before the daemon starts.
        await InstallDockerDropInAsync(cancellationToken);

        // A systemd unit behind the stable 'current' link can change on
        // upgrade or rollback, so reload on every Control activation.
        await _systemd.ReloadAsync(cancellationToken);
    }

    internal async Task RemoveAfterFailedFirstInstallAsync(
        ApplicationRegistration application,
        CancellationToken cancellationToken)
    {
        // A partial first install may have created the Docker drop-in before
        // the helper link was committed. Remove only our exact managed file.
        var dockerDropIn = GetDockerDropInPath();
        if (File.Exists(dockerDropIn))
        {
            var current = await File.ReadAllTextAsync(dockerDropIn, cancellationToken);
            if (current != DockerDropInContents)
            {
                throw new DeploymentExecutionException("Docker's NetworkAccess drop-in differs from the managed policy.");
            }
            File.Delete(dockerDropIn);
        }

        if (!IsInstalled(application))
        {
            await _systemd.ReloadAsync(cancellationToken);
            return;
        }

        // No Control release was previously active. Leave no enabled
        // unit pointing at a now-absent release after activation fails.
        await _systemd.DisableAsync(UnitName, cancellationToken);
        File.Delete(Path.Combine(_systemdUnitDirectory, UnitName));
        await _systemd.ReloadAsync(cancellationToken);
    }

    private string GetDockerDropInPath() =>
        Path.Combine(_systemdUnitDirectory, "docker.service.d", DockerDropInName);

    private async Task InstallDockerDropInAsync(CancellationToken cancellationToken)
    {
        var path = GetDockerDropInPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (File.Exists(path))
        {
            if (await File.ReadAllTextAsync(path, cancellationToken) != DockerDropInContents)
            {
                throw new DeploymentExecutionException("Docker's NetworkAccess drop-in differs from the managed policy.");
            }
            return;
        }

        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, DockerDropInContents, cancellationToken);
            File.Move(temporary, path);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static string GetExpectedTarget(ApplicationRegistration application) =>
        Path.Combine(application.InstallationPath, "current", "network-access", "systemd", UnitName);
}
