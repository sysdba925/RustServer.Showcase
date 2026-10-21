namespace RustServer.Updater.Api.Operations;

/// <summary>
/// Coordinates durable operation lifecycle changes.
/// </summary>
public interface IOperationManager
{
    /// <summary>
    /// Creates a pending operation unless another active operation already exists for the application.
    /// </summary>
    Task<Operation> CreateAsync(CreateOperationRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Gets one operation.
    /// </summary>
    Task<Operation?> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Gets the newest operations.
    /// </summary>
    Task<IReadOnlyCollection<Operation>> GetRecentAsync(int limit, CancellationToken cancellationToken);

    /// <summary>
    /// Marks a pending operation as running before work with external side effects begins.
    /// </summary>
    Task<Operation> StartAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Persists the current safe-resume step of an active operation.
    /// </summary>
    Task<Operation> UpdateStepAsync(Guid id, string step, CancellationToken cancellationToken);

    /// <summary>
    /// Marks a running operation as successfully completed.
    /// </summary>
    Task<Operation> CompleteAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Persists the result produced by an active operation before it reaches a terminal state.
    /// </summary>
    Task<Operation> SetResultAsync(Guid id, PackagePreparationResult result, CancellationToken cancellationToken);

    /// <summary>Persists the executor-confirmed deployment result while the operation is still active.</summary>
    Task<Operation> SetDeploymentResultAsync(
        Guid id,
        Deployments.DeploymentResult result,
        CancellationToken cancellationToken);

    /// <summary>
    /// Marks an active operation as failed and records an error.
    /// </summary>
    Task<Operation> FailAsync(Guid id, string error, CancellationToken cancellationToken);

    /// <summary>
    /// Marks an active operation as interrupted without making assumptions about deployment outcome.
    /// </summary>
    Task<Operation> InterruptAsync(Guid id, string? error, CancellationToken cancellationToken);

    /// <summary>
    /// Runs a supplied operation body with an operation-owned cancellation token.
    /// </summary>
    Task<Operation> ExecuteAsync(
        Guid id,
        Func<CancellationToken, Task> operation,
        CancellationToken operationCancellationToken);

    /// <summary>
    /// Converts records left pending or running by a previous process into interrupted records.
    /// </summary>
    Task<IReadOnlyCollection<Operation>> RecoverIncompleteAsync(CancellationToken cancellationToken);
}
