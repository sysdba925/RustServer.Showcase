namespace RustServer.Updater.Api.Deployments;

/// <summary>Fixed local locations and command used to communicate with the privileged executor.</summary>
public sealed class DeploymentExecutorOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "DeploymentExecutor";

    /// <summary>Directory where the unprivileged API writes one request per operation.</summary>
    public string RequestStoragePath { get; init; } = "/var/lib/rustserver/updater/deployment-requests";

    /// <summary>Directory where the executor writes root-owned transaction records.</summary>
    public string TransactionStoragePath { get; init; } = "/var/lib/rustserver/updater/deployments";

    /// <summary>Root-owned systemd socket that accepts only one operation UUID.</summary>
    public string SocketPath { get; init; } = "/run/rustserver-deployment-executor.sock";
}
