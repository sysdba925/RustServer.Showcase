using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RustServer.Control.Application.Operations;
using RustServer.Control.Application.Servers;
using RustServer.Control.Domain.Operations;

namespace RustServer.Control.Infrastructure.Operations;

/// <summary>
/// Executes accepted Rust update operations one at a time.
/// </summary>
public sealed class RustUpdateOperationWorker : BackgroundService
{
    private const string ServerRunningError = "Rust server must be stopped before it can be updated.";
    private const string UpdateFailedError = "Rust server update failed.";
    private const string UnexpectedFailureError = "The Rust server update could not be completed.";
    private const string ShutdownFailureError = "The Rust server update was interrupted because the control service is stopping.";

    private readonly IOperationStore _operationStore;
    private readonly IRustUpdateOperationQueue _operationQueue;
    private readonly IServerControl _serverControl;
    private readonly IServerMutationLock _serverMutationLock;
    private readonly IRustServerUpdater _updater;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<RustUpdateOperationWorker> _logger;

    public RustUpdateOperationWorker(
        IOperationStore operationStore,
        IRustUpdateOperationQueue operationQueue,
        IServerControl serverControl,
        IServerMutationLock serverMutationLock,
        IRustServerUpdater updater,
        TimeProvider timeProvider,
        ILogger<RustUpdateOperationWorker> logger)
    {
        ArgumentNullException.ThrowIfNull(operationStore);
        ArgumentNullException.ThrowIfNull(operationQueue);
        ArgumentNullException.ThrowIfNull(serverControl);
        ArgumentNullException.ThrowIfNull(serverMutationLock);
        ArgumentNullException.ThrowIfNull(updater);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _operationStore = operationStore;
        _operationQueue = operationQueue;
        _serverControl = serverControl;
        _serverMutationLock = serverMutationLock;
        _updater = updater;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            Guid operationId;
            try
            {
                operationId = await _operationQueue.DequeueAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            await ExecuteOperationAsync(operationId, stoppingToken);
        }
    }

    private async Task ExecuteOperationAsync(Guid operationId, CancellationToken stoppingToken)
    {
        if (_operationStore.Get(operationId) is null)
        {
            _logger.LogWarning("Rust update operation {OperationId} was not found.", operationId);
            return;
        }

        try
        {
            await _serverMutationLock.ExecuteAsync(async () =>
            {
                MarkRunning(operationId);

                // The stopped-state check and SteamCMD execution share the same lock.
                var serverStatus = await _serverControl.GetStatusAsync(stoppingToken);
                if (serverStatus.IsRunning)
                {
                    MarkFailed(operationId, ServerRunningError);
                    return;
                }

                var updateResult = await _updater.UpdateAsync(stoppingToken);
                if (updateResult.Succeeded)
                {
                    MarkSucceeded(operationId, updateResult.ExitCode);
                }
                else
                {
                    MarkFailed(operationId, UpdateFailedError, updateResult.ExitCode);
                }
            }, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            MarkFailed(operationId, ShutdownFailureError);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Rust update operation {OperationId} failed.", operationId);
            MarkFailed(operationId, UnexpectedFailureError);
        }
    }

    private void MarkRunning(Guid operationId)
    {
        var operation = GetOperation(operationId);
        _operationStore.Update(operation with
        {
            Status = OperationStatus.Running,
            StartedAt = _timeProvider.GetUtcNow()
        });
    }

    private void MarkSucceeded(Guid operationId, int exitCode)
    {
        var operation = GetOperation(operationId);
        _operationStore.Update(operation with
        {
            Status = OperationStatus.Succeeded,
            FinishedAt = _timeProvider.GetUtcNow(),
            ExitCode = exitCode,
            Error = null
        });
    }

    private void MarkFailed(Guid operationId, string error, int? exitCode = null)
    {
        var operation = GetOperation(operationId);
        if (operation.Status is OperationStatus.Succeeded or OperationStatus.Failed)
        {
            return;
        }

        _operationStore.Update(operation with
        {
            Status = OperationStatus.Failed,
            FinishedAt = _timeProvider.GetUtcNow(),
            ExitCode = exitCode,
            Error = error
        });
    }

    private Operation GetOperation(Guid operationId) =>
        _operationStore.Get(operationId)
        ?? throw new InvalidOperationException("The operation does not exist.");
}
