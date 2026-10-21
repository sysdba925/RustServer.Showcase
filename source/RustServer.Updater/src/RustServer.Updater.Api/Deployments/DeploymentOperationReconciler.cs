using RustServer.Updater.Api.Applications;
using RustServer.Updater.Api.Operations;

namespace RustServer.Updater.Api.Deployments;

/// <summary>Copies executor-confirmed outcomes into the public durable operation history.</summary>
public interface IDeploymentOperationReconciler
{
    /// <summary>Reconciles every incomplete deployment operation visible to the Updater host.</summary>
    Task ReconcileIncompleteAsync(CancellationToken cancellationToken);

    /// <summary>Reconciles one incomplete deployment operation and returns whether a transaction exists.</summary>
    Task<bool> ReconcileAsync(Operation operation, CancellationToken cancellationToken);
}

/// <summary>Reconciles root-owned transaction files without inferring success from process launch.</summary>
public sealed class DeploymentOperationReconciler(
    IOperationStore operationStore,
    IOperationManager operationManager,
    IDeploymentExecutorClient deploymentExecutorClient) : IDeploymentOperationReconciler
{
    /// <inheritdoc />
    public async Task ReconcileIncompleteAsync(CancellationToken cancellationToken)
    {
        var operations = await operationStore.GetIncompleteAsync(cancellationToken);
        foreach (var operation in operations.Where(IsDeployment))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await ReconcileAsync(operation, cancellationToken);
        }
    }

    /// <inheritdoc />
    public async Task<bool> ReconcileAsync(Operation operation, CancellationToken cancellationToken)
    {
        if (!IsDeployment(operation))
        {
            return false;
        }

        // Callers may have observed the operation before a concurrent worker
        // started it. Re-read its durable state before choosing a transition.
        var currentOperation = await operationManager.GetAsync(operation.Id, cancellationToken);
        if (currentOperation is null || currentOperation.Status is not (OperationStatus.Pending or OperationStatus.Running))
        {
            return currentOperation is not null;
        }
        operation = currentOperation;

        var transaction = await deploymentExecutorClient.GetTransactionAsync(operation.Id, cancellationToken);
        if (transaction is null)
        {
            return false;
        }

        if (!string.Equals(transaction.ApplicationName, operation.ApplicationName, StringComparison.Ordinal))
        {
            return false;
        }

        if (!transaction.IsTerminal)
        {
            await operationManager.UpdateStepAsync(operation.Id, transaction.Step, cancellationToken);
            return true;
        }

        if (operation.Status == OperationStatus.Pending)
        {
            await operationManager.StartAsync(operation.Id, cancellationToken);
        }

        await operationManager.UpdateStepAsync(operation.Id, transaction.Step, cancellationToken);
        await operationManager.SetDeploymentResultAsync(operation.Id, DeploymentApplicationHandler.ToResult(transaction), cancellationToken);
        if (transaction.Status == DeploymentTransactionStatus.Succeeded)
        {
            await operationManager.CompleteAsync(operation.Id, cancellationToken);
        }
        else
        {
            await operationManager.FailAsync(
                operation.Id,
                transaction.Error ?? "Deployment executor restored the previous release.",
                cancellationToken);
        }

        return true;
    }

    /// <summary>Whether an operation is owned by the executor rather than the in-process queue.</summary>
    public static bool IsDeployment(Operation operation) => operation.Type is OperationType.Install
        or OperationType.Update
        or OperationType.Rollback
        or OperationType.SelfUpdate;
}
