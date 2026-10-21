using RustServer.Control.Domain.Operations;
using RustServer.Control.Domain.Wipe;

namespace RustServer.Control.Application.Operations;

/// <summary>
/// Accepts explicit wipe plans when the Rust server is stopped.
/// </summary>
public interface IWipeOperationService
{
    Task<WipeOperationQueueResult> QueueAsync(
        WipePlan plan,
        CancellationToken cancellationToken = default);

    Operation? Get(Guid operationId);
}
