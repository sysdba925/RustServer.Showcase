namespace RustServer.Updater.Api.Operations;

/// <summary>
/// Indicates an attempted operation lifecycle transition is not allowed.
/// </summary>
public sealed class OperationStateTransitionException(Operation operation, OperationStatus targetStatus)
    : InvalidOperationException(
        $"Operation '{operation.Id}' cannot transition from {operation.Status} to {targetStatus}.");
