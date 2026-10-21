using Microsoft.Extensions.Hosting;
using RustServer.Control.Application.NetworkAccess;
using RustServer.Control.Application.Servers;
using RustServer.Control.Application.StartupTracking;

namespace RustServer.Control.Api.NetworkAccess;

/// <summary>
/// Re-applies persisted rules when Control starts and periodically afterwards. This closes the
/// startup race where Docker has started but the Rust container has not yet received its address,
/// and refreshes the maintenance DNAT after Docker assigns a new container address.
/// </summary>
public sealed class NetworkAccessReconciliationService(
    NetworkAccessOrchestrator orchestrator,
    IServerControl serverControl,
    IStartupTrackingService startupTracking,
    ILogger<NetworkAccessReconciliationService> logger) : BackgroundService
{
    private static readonly TimeSpan ReconciliationInterval = TimeSpan.FromSeconds(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(ReconciliationInterval);
        try
        {
            do
            {
                await ReconcileOnceAsync(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal service shutdown.
        }
    }

    private async Task ReconcileOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            var ready = false;
            try
            {
                var status = await serverControl.GetStatusAsync(cancellationToken);
                if (status.IsReady)
                {
                    ready = (await startupTracking.GetSnapshotAsync(cancellationToken)).Started;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogDebug(exception, "Rust readiness is unavailable; retaining the public port guard.");
            }

            await orchestrator.ReconcileAsync(ready, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The host is stopping; the next instance will reconcile the persisted mode.
        }
        catch (Exception exception) when (exception is NetworkAccessSettingsStoreException or NetworkAccessRuleApplicationException)
        {
            logger.LogWarning(
                exception,
                "NetworkAccess reconciliation did not apply the persisted rules; it will retry in {RetryInterval}.",
                ReconciliationInterval);
        }
    }
}
