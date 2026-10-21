using RustServer.Updater.Api.Operations;
using RustServer.Updater.Api.Deployments;

namespace RustServer.Updater.Api.Applications;

/// <summary>Accepts a package-preparation operation and transfers execution to the background queue.</summary>
public sealed class PrepareApplicationHandler(
    IApplicationRegistry applicationRegistry,
    IOperationManager operationManager,
    IBackgroundOperationQueue backgroundOperationQueue,
    IPackagePreparationService packagePreparationService,
    IDeploymentExecutorClient deploymentExecutorClient)
{
    /// <summary>Creates the durable operation before scheduling its independent background work.</summary>
    public async Task<Operation> HandleAsync(
        string applicationName,
        PrepareApplicationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        applicationRegistry.EnsureConfigurationIsCurrent();
        var application = applicationRegistry.GetByName(applicationName)
            ?? throw new ApplicationRegistrationNotFoundException(applicationName);
        if (string.IsNullOrWhiteSpace(request.Version))
        {
            throw new PackagePreparationException("Release version must not be empty.");
        }

        var releaseTag = request.Version.Trim();
        var operation = await operationManager.CreateAsync(
            new CreateOperationRequest(application.Name, OperationType.Prepare, releaseTag, InitialStep: "Resolving"),
            cancellationToken);
        try
        {
            backgroundOperationQueue.Enqueue(operationCancellationToken => operationManager.ExecuteAsync(
                operation.Id,
                async executionCancellationToken =>
                {
                    var result = await packagePreparationService.PrepareAsync(
                        application,
                        releaseTag,
                        (step, stepCancellationToken) => operationManager.UpdateStepAsync(
                            operation.Id,
                            step,
                            stepCancellationToken),
                        executionCancellationToken);
                    await operationManager.UpdateStepAsync(
                        operation.Id,
                        "Securing prepared artifact",
                        executionCancellationToken);
                    var handoff = await deploymentExecutorClient.ExecuteAsync(
                        new DeploymentExecutorRequest(
                            operation.Id,
                            application.Name,
                            OperationType.Prepare,
                            releaseTag),
                        CancellationToken.None);
                    if (handoff.Status != DeploymentTransactionStatus.Succeeded)
                    {
                        throw new DeploymentActivationException(
                            handoff.Error ?? "The root executor could not secure the prepared artifact.");
                    }

                    await operationManager.UpdateStepAsync(
                        operation.Id,
                        handoff.Step,
                        executionCancellationToken);
                    await operationManager.SetResultAsync(operation.Id, result, executionCancellationToken);
                },
                operationCancellationToken));
        }
        catch
        {
            await operationManager.FailAsync(
                operation.Id,
                "Package preparation could not be queued.",
                CancellationToken.None);
            throw;
        }

        return operation;
    }
}
