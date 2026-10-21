namespace RustServer.Control.Application.Operations;

/// <summary>
/// Queues accepted Oxide update operations for background execution.
/// </summary>
public interface IOxideUpdateOperationQueue
{
    void Enqueue(Guid operationId);

    ValueTask<Guid> DequeueAsync(CancellationToken cancellationToken = default);
}
