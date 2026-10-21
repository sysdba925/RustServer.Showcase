using RustServer.Updater.Api.Applications;
using RustServer.Updater.Api.Deployments;
using RustServer.Updater.Api.Operations;

namespace RustServer.Updater.Api.Emergency;

/// <summary>Runs verified preparation and activation as one emergency Update operation.</summary>
public sealed class EmergencyUpdateApplicationHandler(
    IReleaseChannelProvider releaseChannelProvider,
    IOperationManager operationManager,
    IBackgroundOperationQueue backgroundOperationQueue,
    IPackagePreparationService packagePreparationService,
    DeploymentApplicationHandler deploymentApplicationHandler)
{
    /// <summary>Accepts an installed application's selected channel release for background preparation and activation.</summary>
    public async Task<Operation> HandleAsync(
        string applicationName,
        EmergencyUpdateApplicationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Version))
        {
            throw new PackagePreparationException("Release version must not be empty.");
        }

        var version = request.Version.Trim();
        ReleaseChannelPolicy.EnsureAllowedTag(version, releaseChannelProvider.GetChannel());
        var context = await deploymentApplicationHandler.CreateEmergencyUpdateAsync(
            applicationName,
            version,
            cancellationToken);
        try
        {
            backgroundOperationQueue.Enqueue(cancellationToken => ExecuteAsync(context, cancellationToken));
        }
        catch
        {
            await TryFailAsync(context.Operation.Id, "Emergency update could not be queued.");
            throw;
        }

        return context.Operation;
    }

    private async Task ExecuteAsync(DeploymentOperationContext context, CancellationToken cancellationToken)
    {
        try
        {
            var targetVersion = context.TargetVersion
                ?? throw new InvalidOperationException("An emergency update requires a concrete target version.");
            await operationManager.StartAsync(context.Operation.Id, cancellationToken);
            var result = await packagePreparationService.PrepareAsync(
                context.Application,
                targetVersion,
                (step, stepCancellationToken) => operationManager.UpdateStepAsync(
                    context.Operation.Id,
                    $"Preparing: {step}",
                    stepCancellationToken),
                cancellationToken);
            await operationManager.SetResultAsync(context.Operation.Id, result, cancellationToken);
            await deploymentApplicationHandler.EnsurePreparedPackageAsync(
                context.Application,
                targetVersion,
                cancellationToken);
            await deploymentApplicationHandler.ExecuteDeploymentAsync(context, cancellationToken);
            await operationManager.CompleteAsync(context.Operation.Id, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // A deployment executor transaction is reconciled independently after host shutdown.
        }
        catch (Exception exception)
        {
            await TryFailAsync(context.Operation.Id, exception.Message);
        }
    }

    private async Task TryFailAsync(Guid operationId, string error)
    {
        try
        {
            await operationManager.FailAsync(
                operationId,
                string.IsNullOrWhiteSpace(error) ? "Emergency update failed without an error message." : error,
                CancellationToken.None);
        }
        catch (OperationStateTransitionException)
        {
            // The deployment reconciler already recorded the terminal executor outcome.
        }
    }
}
