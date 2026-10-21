namespace RustServer.Updater.Api.Operations;

/// <summary>
/// The information needed to create an operation record.
/// </summary>
public sealed record CreateOperationRequest(
    string ApplicationName,
    OperationType Type,
    string? TargetVersion = null,
    string? PreviousVersion = null,
    string? InitialStep = null);
