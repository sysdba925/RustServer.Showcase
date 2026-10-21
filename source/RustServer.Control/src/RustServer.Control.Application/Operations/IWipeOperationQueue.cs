using RustServer.Control.Domain.Wipe;

namespace RustServer.Control.Application.Operations;

/// <summary>
/// Delivers accepted wipe operation identifiers to the single wipe worker.
/// </summary>
public interface IWipeOperationQueue
{
    void Enqueue(Guid operationId, WipePlan plan);

    ValueTask<WipeQueuedOperation> DequeueAsync(CancellationToken cancellationToken = default);
}

public sealed record WipeQueuedOperation(Guid OperationId, WipePlan Plan);
