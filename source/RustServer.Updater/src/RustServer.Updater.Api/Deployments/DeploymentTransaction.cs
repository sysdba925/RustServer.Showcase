namespace RustServer.Updater.Api.Deployments;

/// <summary>Executor-owned durable outcome of a deployment transaction.</summary>
public sealed record DeploymentTransaction(
    Guid OperationId,
    string ApplicationName,
    DeploymentTransactionStatus Status,
    string Step,
    string? PreviousVersion,
    string? TargetVersion,
    string? ActiveVersion,
    string? Error,
    DateTimeOffset UpdatedAt,
    string? ProvisioningOutput = null,
    int? ProvisioningExitCode = null)
{
    /// <summary>Whether the executor can no longer change this transaction.</summary>
    public bool IsTerminal => Status is DeploymentTransactionStatus.Succeeded
        or DeploymentTransactionStatus.RolledBack
        or DeploymentTransactionStatus.Failed;
}

/// <summary>Executor transaction states persisted independently of the Updater host.</summary>
public enum DeploymentTransactionStatus
{
    Pending,
    Activating,
    Succeeded,
    RolledBack,
    Failed
}
