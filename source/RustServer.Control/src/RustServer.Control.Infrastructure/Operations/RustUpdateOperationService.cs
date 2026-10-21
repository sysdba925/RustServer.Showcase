using RustServer.Control.Application.Operations;
using RustServer.Control.Application.Servers;
using RustServer.Control.Domain.Operations;

namespace RustServer.Control.Infrastructure.Operations;

/// <summary>
/// Coordinates the acceptance of Rust update operations.
/// </summary>
public sealed class RustUpdateOperationService : IRustUpdateOperationService
{
    private readonly IOperationStore _operationStore;
    private readonly IRustUpdateOperationQueue _operationQueue;
    private readonly IServerControl _serverControl;

    public RustUpdateOperationService(
        IOperationStore operationStore,
        IRustUpdateOperationQueue operationQueue,
        IServerControl serverControl)
    {
        ArgumentNullException.ThrowIfNull(operationStore);
        ArgumentNullException.ThrowIfNull(operationQueue);
        ArgumentNullException.ThrowIfNull(serverControl);

        _operationStore = operationStore;
        _operationQueue = operationQueue;
        _serverControl = serverControl;
    }

    public async Task<RustUpdateOperationQueueResult> QueueAsync(CancellationToken cancellationToken = default)
    {
        // This is only an early rejection. The worker repeats the check under the mutation lock.
        if (_operationStore.HasActiveServerMutation())
        {
            return RustUpdateOperationQueueResult.Rejected(
                RustUpdateOperationQueueStatus.OperationInProgress);
        }

        var serverStatus = await _serverControl.GetStatusAsync(cancellationToken);
        if (serverStatus.IsRunning)
        {
            return RustUpdateOperationQueueResult.Rejected(
                RustUpdateOperationQueueStatus.ServerRunning);
        }

        var operation = _operationStore.TryCreateServerMutation(OperationType.RustUpdate);
        if (operation is null)
        {
            return RustUpdateOperationQueueResult.Rejected(
                RustUpdateOperationQueueStatus.OperationInProgress);
        }

        _operationQueue.Enqueue(operation.Id);

        return RustUpdateOperationQueueResult.Accepted(operation);
    }

    public Operation? Get(Guid operationId) => _operationStore.Get(operationId);
}
