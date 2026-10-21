using RustServer.Control.Domain.Operations;

namespace RustServer.Control.Application.Operations;

/// <summary>
/// Accepts Rust update operations and exposes their current state.
/// </summary>
public interface IRustUpdateOperationService
{
    /// <summary>
    /// Queues an update when the Rust server is currently stopped and no server-mutating update is active.
    /// </summary>
    Task<RustUpdateOperationQueueResult> QueueAsync(CancellationToken cancellationToken = default);

    Operation? Get(Guid operationId);
}
