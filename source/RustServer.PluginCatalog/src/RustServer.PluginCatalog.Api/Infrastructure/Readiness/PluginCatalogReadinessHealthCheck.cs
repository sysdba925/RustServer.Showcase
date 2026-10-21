using Microsoft.Extensions.Diagnostics.HealthChecks;
using RustServer.PluginCatalog.Api.Features.PluginCatalog.Ports;

namespace RustServer.PluginCatalog.Api.Infrastructure.Readiness;

/// <summary>
/// Verifies the dependencies that are required to serve catalog traffic without
/// creating schema or persistent media as a side effect.
/// </summary>
public sealed class PluginCatalogReadinessHealthCheck(
    IServiceScopeFactory serviceScopeFactory) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var scope = serviceScopeFactory.CreateScope();
            var databaseReadinessProbe = scope.ServiceProvider
                .GetRequiredService<IPluginCatalogDatabaseReadinessProbe>();
            var mediaStorageReadinessProbe = scope.ServiceProvider
                .GetRequiredService<IPluginMediaStorageReadinessProbe>();
            await databaseReadinessProbe.VerifyReadyAsync(cancellationToken);
            await mediaStorageReadinessProbe.VerifyReadyAsync(cancellationToken);
            return HealthCheckResult.Healthy("Catalog database and media storage are ready.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy(
                "Catalog database or media storage is unavailable.",
                exception);
        }
    }
}
