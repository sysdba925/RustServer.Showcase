using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace RustServer.Updater.Api.Operations;

/// <summary>
/// Recovers durable records that were left active by an earlier updater process.
/// </summary>
public sealed class OperationRecoveryService(
    IOperationManager operationManager,
    IOperationStore operationStore,
    Deployments.IDeploymentOperationReconciler deploymentOperationReconciler,
    ILogger<OperationRecoveryService> logger) : IHostedService
{
    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var incompleteOperations = await operationStore.GetIncompleteAsync(cancellationToken);
        var interruptedOperations = new List<Operation>();
        foreach (var operation in incompleteOperations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Deployments.DeploymentOperationReconciler.IsDeployment(operation)
                && await deploymentOperationReconciler.ReconcileAsync(operation, cancellationToken))
            {
                // An independent executor is still working or has recorded its actual outcome.
                continue;
            }

            interruptedOperations.Add(await operationManager.InterruptAsync(
                operation.Id,
                "Updater restarted before the operation reached a terminal state.",
                cancellationToken));
        }
        foreach (var operation in interruptedOperations)
        {
            logger.LogWarning(
                "Marked operation {OperationId} for application {ApplicationName} as interrupted after updater startup recovery.",
                operation.Id,
                operation.ApplicationName);
        }
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
