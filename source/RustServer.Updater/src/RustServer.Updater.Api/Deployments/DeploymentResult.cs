namespace RustServer.Updater.Api.Deployments;

/// <summary>Deployment outcome copied from the executor's root-owned transaction record.</summary>
public sealed record DeploymentResult(
    string? PreviousVersion,
    string? TargetVersion,
    string? ActiveVersion,
    bool RolledBack,
    DateTimeOffset RecordedAt,
    string? ProvisioningOutput = null,
    int? ProvisioningExitCode = null);
