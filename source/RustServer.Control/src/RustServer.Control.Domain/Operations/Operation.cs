namespace RustServer.Control.Domain.Operations;

using RustServer.Control.Domain.Wipe;

public sealed record Operation(
    Guid Id,
    OperationType Type,
    OperationStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    int? ExitCode,
    string? Error,
    WipeExecutionReport? WipeResult = null);
