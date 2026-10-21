using RustServer.Control.Domain.Operations;

namespace RustServer.Control.Application.Operations;

public sealed record OxideUpdateOperationQueueResult(
    OxideUpdateOperationQueueStatus Status,
    Operation? Operation)
{
    public static OxideUpdateOperationQueueResult Accepted(Operation operation) =>
        new(OxideUpdateOperationQueueStatus.Accepted, operation);

    public static OxideUpdateOperationQueueResult Rejected(OxideUpdateOperationQueueStatus status) =>
        new(status, null);
}

public enum OxideUpdateOperationQueueStatus
{
    Accepted,
    ServerRunning,
    OperationInProgress
}
