using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace RustServer.Updater.Api.Operations;

/// <summary>
/// Enforces operation lifecycle invariants and persists every state transition before returning it.
/// </summary>
public sealed class OperationManager(
    IOperationStore operationStore,
    ILogger<OperationManager> logger) : IOperationManager
{
    private const string DefaultInitialStep = "Created";
    private const string CancellationMessage = "Operation cancellation was requested.";
    private const string RecoveryMessage = "Updater restarted before the operation reached a terminal state.";

    private readonly ConcurrentDictionary<string, SemaphoreSlim> _applicationLocks =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _operationLocks = new();

    /// <inheritdoc />
    public async Task<Operation> CreateAsync(CreateOperationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!Enum.IsDefined(request.Type))
        {
            throw new ArgumentOutOfRangeException(nameof(request), request.Type, "Operation type is invalid.");
        }

        var applicationName = ValidateApplicationName(request.ApplicationName);
        var applicationLock = _applicationLocks.GetOrAdd(applicationName, static _ => new SemaphoreSlim(1, 1));
        await applicationLock.WaitAsync(cancellationToken);

        try
        {
            var activeOperation = (await operationStore.GetIncompleteAsync(cancellationToken))
                .FirstOrDefault(operation =>
                    string.Equals(operation.ApplicationName, applicationName, StringComparison.OrdinalIgnoreCase));
            if (activeOperation is not null)
            {
                throw new OperationConflictException(applicationName);
            }

            var operation = new Operation(
                Guid.NewGuid(),
                applicationName,
                request.Type,
                OperationStatus.Pending,
                NormalizeStep(request.InitialStep ?? DefaultInitialStep),
                NormalizeOptionalValue(request.TargetVersion),
                NormalizeOptionalValue(request.PreviousVersion),
                DateTimeOffset.UtcNow,
                null,
                null,
                null);

            await operationStore.CreateAsync(operation, cancellationToken);
            logger.LogInformation(
                "Created operation {OperationId} of type {OperationType} for application {ApplicationName}.",
                operation.Id,
                operation.Type,
                operation.ApplicationName);
            return operation;
        }
        finally
        {
            applicationLock.Release();
        }
    }

    /// <inheritdoc />
    public Task<Operation?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        operationStore.GetAsync(id, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyCollection<Operation>> GetRecentAsync(int limit, CancellationToken cancellationToken) =>
        operationStore.GetRecentAsync(limit, cancellationToken);

    /// <inheritdoc />
    public Task<Operation> StartAsync(Guid id, CancellationToken cancellationToken) =>
        ChangeAsync(
            id,
            OperationStatus.Running,
            operation => operation with
            {
                Status = OperationStatus.Running,
                StartedAt = operation.StartedAt ?? DateTimeOffset.UtcNow,
                Error = null
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<Operation> UpdateStepAsync(Guid id, string step, CancellationToken cancellationToken)
    {
        var normalizedStep = NormalizeStep(step);
        return ChangeAsync(
            id,
            targetStatus: null,
            operation => operation with { Step = normalizedStep },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<Operation> CompleteAsync(Guid id, CancellationToken cancellationToken) =>
        ChangeAsync(
            id,
            OperationStatus.Succeeded,
            operation => operation with
            {
                Status = OperationStatus.Succeeded,
                CompletedAt = DateTimeOffset.UtcNow,
                Error = null
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<Operation> SetResultAsync(Guid id, PackagePreparationResult result, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(result);
        return ChangeAsync(
            id,
            targetStatus: null,
            operation => operation with { Result = result },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<Operation> SetDeploymentResultAsync(
        Guid id,
        Deployments.DeploymentResult result,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(result);
        return ChangeAsync(
            id,
            targetStatus: null,
            operation => operation with { Deployment = result },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<Operation> FailAsync(Guid id, string error, CancellationToken cancellationToken)
    {
        var normalizedError = NormalizeError(error);
        return ChangeAsync(
            id,
            OperationStatus.Failed,
            operation => operation with
            {
                Status = OperationStatus.Failed,
                CompletedAt = DateTimeOffset.UtcNow,
                Error = normalizedError
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<Operation> InterruptAsync(Guid id, string? error, CancellationToken cancellationToken) =>
        ChangeAsync(
            id,
            OperationStatus.Interrupted,
            operation => operation with
            {
                Status = OperationStatus.Interrupted,
                CompletedAt = DateTimeOffset.UtcNow,
                Error = NormalizeOptionalValue(error)
            },
            cancellationToken);

    /// <inheritdoc />
    public async Task<Operation> ExecuteAsync(
        Guid id,
        Func<CancellationToken, Task> operation,
        CancellationToken operationCancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);

        await StartAsync(id, operationCancellationToken);
        try
        {
            await operation(operationCancellationToken);
            return await CompleteAsync(id, operationCancellationToken);
        }
        catch (OperationCanceledException) when (operationCancellationToken.IsCancellationRequested)
        {
            await InterruptAsync(id, CancellationMessage, CancellationToken.None);
            throw;
        }
        catch (Exception exception)
        {
            var error = string.IsNullOrWhiteSpace(exception.Message)
                ? "Operation failed without an error message."
                : exception.Message;
            await FailAsync(id, error, CancellationToken.None);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyCollection<Operation>> RecoverIncompleteAsync(CancellationToken cancellationToken)
    {
        var incompleteOperations = await operationStore.GetIncompleteAsync(cancellationToken);
        var interruptedOperations = new List<Operation>(incompleteOperations.Count);

        foreach (var operation in incompleteOperations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            interruptedOperations.Add(await InterruptAsync(operation.Id, RecoveryMessage, cancellationToken));
        }

        return interruptedOperations;
    }

    private async Task<Operation> ChangeAsync(
        Guid id,
        OperationStatus? targetStatus,
        Func<Operation, Operation> change,
        CancellationToken cancellationToken)
    {
        var operationLock = _operationLocks.GetOrAdd(id, static _ => new SemaphoreSlim(1, 1));
        await operationLock.WaitAsync(cancellationToken);

        try
        {
            var operation = await operationStore.GetAsync(id, cancellationToken)
                ?? throw new OperationNotFoundException(id);

            EnsureTransitionAllowed(operation, targetStatus);
            var changedOperation = change(operation);
            await operationStore.SaveAsync(changedOperation, cancellationToken);
            return changedOperation;
        }
        finally
        {
            operationLock.Release();
        }
    }

    private static void EnsureTransitionAllowed(Operation operation, OperationStatus? targetStatus)
    {
        if (targetStatus is null)
        {
            if (operation.Status is OperationStatus.Pending or OperationStatus.Running)
            {
                return;
            }

            throw new OperationStateTransitionException(operation, operation.Status);
        }

        if (targetStatus == OperationStatus.Succeeded
            && operation.Type == OperationType.Prepare
            && operation.Result is null)
        {
            throw new InvalidOperationException("A package preparation operation requires a durable staging result before it can succeed.");
        }

        var isAllowed = (operation.Status, targetStatus.Value) switch
        {
            (OperationStatus.Pending, OperationStatus.Running) => true,
            (OperationStatus.Pending, OperationStatus.Failed) => true,
            (OperationStatus.Pending, OperationStatus.Interrupted) => true,
            (OperationStatus.Running, OperationStatus.Succeeded) => true,
            (OperationStatus.Running, OperationStatus.Failed) => true,
            (OperationStatus.Running, OperationStatus.Interrupted) => true,
            _ => false
        };

        if (!isAllowed)
        {
            throw new OperationStateTransitionException(operation, targetStatus.Value);
        }
    }

    private static string ValidateApplicationName(string applicationName)
    {
        if (string.IsNullOrWhiteSpace(applicationName))
        {
            throw new ArgumentException("Application name must not be empty.", nameof(applicationName));
        }

        return applicationName.Trim();
    }

    private static string NormalizeStep(string step)
    {
        if (string.IsNullOrWhiteSpace(step))
        {
            throw new ArgumentException("Operation step must not be empty.", nameof(step));
        }

        return step.Trim();
    }

    private static string NormalizeError(string error)
    {
        if (string.IsNullOrWhiteSpace(error))
        {
            throw new ArgumentException("Operation error must not be empty.", nameof(error));
        }

        return error.Trim();
    }

    private static string? NormalizeOptionalValue(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
