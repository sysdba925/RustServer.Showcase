using RustServer.Updater.Api.Operations;

namespace RustServer.Updater.Api.Deployments;

/// <summary>Unprivileged request consumed by the executor using an operation identifier as its only argument.</summary>
public sealed record DeploymentExecutorRequest(
    Guid OperationId,
    string ApplicationName,
    OperationType Type,
    string? TargetVersion);
