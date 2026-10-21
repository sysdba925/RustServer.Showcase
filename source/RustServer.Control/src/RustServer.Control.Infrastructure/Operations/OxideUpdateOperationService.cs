using RustServer.Control.Application.Operations;
using RustServer.Control.Application.Servers;
using RustServer.Control.Domain.Operations;

namespace RustServer.Control.Infrastructure.Operations;

/// <summary>
/// Coordinates the acceptance of Oxide update operations.
/// </summary>
public sealed class OxideUpdateOperationService : IOxideUpdateOperationService
{
    private readonly IOperationStore _operationStore;
    private readonly IOxideUpdateOperationQueue _operationQueue;
    private readonly IServerControl _serverControl;

    public OxideUpdateOperationService(
        IOperationStore operationStore,
        IOxideUpdateOperationQueue operationQueue,
        IServerControl serverControl)
    {
        ArgumentNullException.ThrowIfNull(operationStore);
        ArgumentNullException.ThrowIfNull(operationQueue);
        ArgumentNullException.ThrowIfNull(serverControl);

        _operationStore = operationStore;
        _operationQueue = operationQueue;
        _serverControl = serverControl;
    }

    public async Task<OxideUpdateOperationQueueResult> QueueAsync(CancellationToken cancellationToken = default)
    {
        // This is only an early rejection. The worker repeats the check under the mutation lock.
        if (_operationStore.HasActiveServerMutation())
        {
            return OxideUpdateOperationQueueResult.Rejected(
                OxideUpdateOperationQueueStatus.OperationInProgress);
        }

        var serverStatus = await _serverControl.GetStatusAsync(cancellationToken);
        if (serverStatus.IsRunning)
        {
            return OxideUpdateOperationQueueResult.Rejected(
                OxideUpdateOperationQueueStatus.ServerRunning);
        }

        var operation = _operationStore.TryCreateServerMutation(OperationType.OxideUpdate);
        if (operation is null)
        {
            return OxideUpdateOperationQueueResult.Rejected(
                OxideUpdateOperationQueueStatus.OperationInProgress);
        }

        _operationQueue.Enqueue(operation.Id);

        return OxideUpdateOperationQueueResult.Accepted(operation);
    }

    public Operation? Get(Guid operationId) => _operationStore.Get(operationId);
}
