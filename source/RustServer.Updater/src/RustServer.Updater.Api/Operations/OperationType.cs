namespace RustServer.Updater.Api.Operations;

/// <summary>
/// The kind of deployment-related work represented by an operation.
/// </summary>
public enum OperationType
{
    /// <summary>Downloads, verifies, and stages a release without activating it.</summary>
    Prepare,
    Install,
    Update,
    Rollback,
    SelfUpdate
}
