namespace RustServer.Control.Application.Operations;

/// <summary>
/// Queues accepted Rust update operations for background execution.
/// </summary>
public interface IRustUpdateOperationQueue
{
    void Enqueue(Guid operationId);

    ValueTask<Guid> DequeueAsync(CancellationToken cancellationToken = default);
}
