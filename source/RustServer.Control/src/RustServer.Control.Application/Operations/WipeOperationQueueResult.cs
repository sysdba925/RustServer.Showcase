using RustServer.Control.Domain.Operations;

namespace RustServer.Control.Application.Operations;

public sealed record WipeOperationQueueResult(
    WipeOperationQueueStatus Status,
    Operation? Operation,
    string? ValidationError)
{
    public static WipeOperationQueueResult Accepted(Operation operation) =>
        new(WipeOperationQueueStatus.Accepted, operation, null);

    public static WipeOperationQueueResult Rejected(WipeOperationQueueStatus status) =>
        new(status, null, null);

    public static WipeOperationQueueResult InvalidPlan(string validationError) =>
        new(WipeOperationQueueStatus.InvalidPlan, null, validationError);
}

public enum WipeOperationQueueStatus
{
    Accepted,
    ServerRunning,
    OperationInProgress,
    InvalidPlan
}
