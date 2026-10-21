namespace RustServer.Updater.Api.Operations;

/// <summary>
/// A durable record of work requested for a registered application.
/// </summary>
public sealed record Operation(
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
    PackagePreparationResult? Result = null,
    Deployments.DeploymentResult? Deployment = null);
