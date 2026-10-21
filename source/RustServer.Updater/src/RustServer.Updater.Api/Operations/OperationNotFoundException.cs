namespace RustServer.Updater.Api.Operations;

/// <summary>
/// Indicates an operation lifecycle change targeted a missing record.
/// </summary>
public sealed class OperationNotFoundException(Guid operationId)
    : KeyNotFoundException($"Operation '{operationId}' was not found.");
