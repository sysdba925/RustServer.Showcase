using RustServer.Control.Domain.Operations;

namespace RustServer.Control.Application.Operations;

public sealed record RustUpdateOperationQueueResult(
    RustUpdateOperationQueueStatus Status,
    Operation? Operation)
{
    public static RustUpdateOperationQueueResult Accepted(Operation operation) =>
        new(RustUpdateOperationQueueStatus.Accepted, operation);

    public static RustUpdateOperationQueueResult Rejected(RustUpdateOperationQueueStatus status) =>
        new(status, null);
}

public enum RustUpdateOperationQueueStatus
{
    Accepted,
    ServerRunning,
    OperationInProgress
}
