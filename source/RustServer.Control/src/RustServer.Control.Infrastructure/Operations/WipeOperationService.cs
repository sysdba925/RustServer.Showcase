using RustServer.Control.Application.Operations;
using RustServer.Control.Application.Servers;
using RustServer.Control.Application.Wipe;
using RustServer.Control.Domain.Operations;
using RustServer.Control.Domain.Wipe;

namespace RustServer.Control.Infrastructure.Operations;

/// <summary>
/// Coordinates acceptance of explicit wipe operations.
/// </summary>
public sealed class WipeOperationService : IWipeOperationService
{
    private readonly IOperationStore _operationStore;
    private readonly IWipeOperationQueue _operationQueue;
    private readonly IServerControl _serverControl;
    private readonly IWipeEngine _wipeEngine;

    public WipeOperationService(
        IOperationStore operationStore,
        IWipeOperationQueue operationQueue,
        IServerControl serverControl,
        IWipeEngine wipeEngine)
    {
        ArgumentNullException.ThrowIfNull(operationStore);
        ArgumentNullException.ThrowIfNull(operationQueue);
        ArgumentNullException.ThrowIfNull(serverControl);
        ArgumentNullException.ThrowIfNull(wipeEngine);

        _operationStore = operationStore;
        _operationQueue = operationQueue;
        _serverControl = serverControl;
        _wipeEngine = wipeEngine;
    }

    public async Task<WipeOperationQueueResult> QueueAsync(
        WipePlan plan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var validationError = _wipeEngine.ValidatePlan(plan);
        if (validationError is not null)
        {
            return WipeOperationQueueResult.InvalidPlan(validationError);
        }

        // This is only an early rejection. The worker repeats the stopped-state check under
        // the same mutation lock that protects lifecycle and update operations.
        if (_operationStore.HasActiveServerMutation())
        {
            return WipeOperationQueueResult.Rejected(WipeOperationQueueStatus.OperationInProgress);
        }

        var serverStatus = await _serverControl.GetStatusAsync(cancellationToken);
        if (serverStatus.IsRunning)
        {
            return WipeOperationQueueResult.Rejected(WipeOperationQueueStatus.ServerRunning);
        }

        var operation = _operationStore.TryCreateServerMutation(OperationType.Wipe);
        if (operation is null)
        {
            return WipeOperationQueueResult.Rejected(WipeOperationQueueStatus.OperationInProgress);
        }

        _operationQueue.Enqueue(operation.Id, plan);

        return WipeOperationQueueResult.Accepted(operation);
    }

    public Operation? Get(Guid operationId) => _operationStore.Get(operationId);
}
