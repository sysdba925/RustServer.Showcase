namespace RustServer.Updater.Api.Operations;

/// <summary>
/// The lifecycle state of an operation.
/// </summary>
public enum OperationStatus
{
    Pending,
    Running,
    Succeeded,
    Failed,
    Interrupted
}
