using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RustServer.Control.Application.Operations;
using RustServer.Control.Application.Servers;
using RustServer.Control.Domain.Operations;

namespace RustServer.Control.Infrastructure.Operations;

/// <summary>
/// Executes accepted Oxide update operations one at a time.
/// </summary>
public sealed class OxideUpdateOperationWorker : BackgroundService
{
    private const string ServerRunningError = "Rust server must be stopped before Oxide can be updated.";
    private const string UpdateFailedError = "Oxide update failed.";
    private const string UnexpectedFailureError = "The Oxide update could not be completed.";
    private const string ShutdownFailureError = "The Oxide update was interrupted because the control service is stopping.";

    private readonly IOperationStore _operationStore;
    private readonly IOxideUpdateOperationQueue _operationQueue;
    private readonly IServerControl _serverControl;
    private readonly IServerMutationLock _serverMutationLock;
    private readonly IOxideUpdater _updater;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<OxideUpdateOperationWorker> _logger;

    public OxideUpdateOperationWorker(
        IOperationStore operationStore,
        IOxideUpdateOperationQueue operationQueue,
        IServerControl serverControl,
        IServerMutationLock serverMutationLock,
        IOxideUpdater updater,
        TimeProvider timeProvider,
        ILogger<OxideUpdateOperationWorker> logger)
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
            _logger.LogWarning("Oxide update operation {OperationId} was not found.", operationId);
            return;
        }

        try
        {
            await _serverMutationLock.ExecuteAsync(async () =>
            {
                MarkRunning(operationId);

                // The stopped-state check and Oxide installation share the same lock.
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
            _logger.LogError(exception, "Oxide update operation {OperationId} failed.", operationId);
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
