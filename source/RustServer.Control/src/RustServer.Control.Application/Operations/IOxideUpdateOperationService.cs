using RustServer.Control.Domain.Operations;

namespace RustServer.Control.Application.Operations;

/// <summary>
/// Accepts Oxide update operations and exposes their current state.
/// </summary>
public interface IOxideUpdateOperationService
{
    /// <summary>
    /// Queues an update when the Rust server is currently stopped and no server-mutating update is active.
    /// </summary>
    Task<OxideUpdateOperationQueueResult> QueueAsync(CancellationToken cancellationToken = default);

    Operation? Get(Guid operationId);
}
