namespace RustServer.Updater.Executor;

/// <summary>Fixed local paths used by the root-owned deployment executor.</summary>
public sealed class DeploymentExecutorOptions
{
    /// <summary>Directory containing API-created operation requests.</summary>
    public string RequestStoragePath { get; init; } = "/var/lib/rustserver/updater/deployment-requests";

    /// <summary>Directory containing root-owned transaction records.</summary>
    public string TransactionStoragePath { get; init; } = "/var/lib/rustserver/updater/deployments";

    /// <summary>Directory containing Stage 6 packages.</summary>
    public string PackageStoragePath { get; init; } = "/var/lib/rustserver/updater/staging";

    /// <summary>Runtime application registry, owned by the Updater after provisioning.</summary>
    public string ApplicationRegistryPath { get; init; } = "/etc/rustserver/updater/applications.ini";

    /// <summary>Cross-process deployment and recovery lock.</summary>
    public string LockPath { get; init; } = "/run/lock/rustserver/updater.lock";

    /// <summary>Directory used for installed systemd unit files.</summary>
    public string SystemdUnitDirectory { get; init; } = "/etc/systemd/system";

    /// <summary>Logical application registration representing this Updater bundle.</summary>
    public string UpdaterApplicationName { get; init; } = "RustServer.Updater";
}
