namespace RustServer.Updater.Api.Operations;

/// <summary>
/// API representation of a durable operation record.
/// </summary>
public sealed record OperationResponse(
    Guid Id,
    string ApplicationName,
    OperationType Type,
    OperationStatus Status,
    string Step,
    string? TargetVersion,
    string? PreviousVersion,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    string? Error,
    PackagePreparationResult? Result,
    Deployments.DeploymentResult? Deployment)
{
    /// <summary>
    /// Converts a domain operation to its public API representation.
    /// </summary>
    public static OperationResponse From(Operation operation) => new(
        operation.Id,
        operation.ApplicationName,
        operation.Type,
        operation.Status,
        operation.Step,
        operation.TargetVersion,
        operation.PreviousVersion,
        operation.CreatedAt,
        operation.StartedAt,
        operation.CompletedAt,
        operation.Error,
        operation.Result,
        operation.Deployment);
}
