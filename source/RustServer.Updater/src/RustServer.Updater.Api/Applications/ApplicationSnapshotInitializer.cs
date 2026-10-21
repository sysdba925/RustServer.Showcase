using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace RustServer.Updater.Api.Applications;

/// <summary>
/// Performs the initial local state check when the updater starts.
/// </summary>
public sealed class ApplicationSnapshotInitializer(
    RefreshLocalApplicationStateHandler refreshLocalApplicationStateHandler,
    ILogger<ApplicationSnapshotInitializer> logger) : IHostedService
{
    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await refreshLocalApplicationStateHandler.HandleAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "The initial local application-state check failed.");
        }
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
