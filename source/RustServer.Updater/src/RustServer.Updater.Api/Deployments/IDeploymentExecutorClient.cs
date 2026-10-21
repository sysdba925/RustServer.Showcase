namespace RustServer.Updater.Api.Deployments;

/// <summary>Starts and observes a fixed-function privileged deployment executor.</summary>
public interface IDeploymentExecutorClient
{
    /// <summary>Writes a request and waits for the executor launcher to report a durable outcome.</summary>
    Task<DeploymentTransaction> ExecuteAsync(DeploymentExecutorRequest request, CancellationToken cancellationToken);

    /// <summary>Reads an executor-owned transaction record when one exists.</summary>
    Task<DeploymentTransaction?> GetTransactionAsync(Guid operationId, CancellationToken cancellationToken);
}
