using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace RustServer.Updater.Api.Deployments;

/// <summary>Observes executor transactions after an Updater crash or self-update.</summary>
public sealed class DeploymentOperationReconciliationService(
    IDeploymentOperationReconciler reconciler,
    ILogger<DeploymentOperationReconciliationService> logger) : BackgroundService
{
    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await reconciler.ReconcileIncompleteAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Could not reconcile deployment executor transactions.");
            }

            await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
        }
    }
}
