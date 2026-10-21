using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RustServer.Control.Application.Operations;
using RustServer.Control.Application.Servers;
using RustServer.Control.Application.Wipe;
using RustServer.Control.Domain.Operations;
using RustServer.Control.Domain.Wipe;

namespace RustServer.Control.Infrastructure.Operations;

/// <summary>
/// Executes accepted wipe plans one at a time, only after rechecking that Rust is stopped.
/// </summary>
public sealed class WipeOperationWorker : BackgroundService
{
    private const string ServerRunningError = "Rust server must be stopped before it can be wiped.";
    private const string WipeFailedError = "Wipe operation failed.";
    private const string UnexpectedFailureError = "The wipe operation could not be completed.";
    private const string ShutdownFailureError = "The wipe operation was interrupted because the control service is stopping.";

    private readonly IOperationStore _operationStore;
    private readonly IWipeOperationQueue _operationQueue;
    private readonly IServerControl _serverControl;
    private readonly IServerMutationLock _serverMutationLock;
    private readonly IWipeEngine _wipeEngine;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<WipeOperationWorker> _logger;

    public WipeOperationWorker(
        IOperationStore operationStore,
        IWipeOperationQueue operationQueue,
        IServerControl serverControl,
        IServerMutationLock serverMutationLock,
        IWipeEngine wipeEngine,
        TimeProvider timeProvider,
        ILogger<WipeOperationWorker> logger)
    {
        ArgumentNullException.ThrowIfNull(operationStore);
        ArgumentNullException.ThrowIfNull(operationQueue);
        ArgumentNullException.ThrowIfNull(serverControl);
        ArgumentNullException.ThrowIfNull(serverMutationLock);
        ArgumentNullException.ThrowIfNull(wipeEngine);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _operationStore = operationStore;
        _operationQueue = operationQueue;
        _serverControl = serverControl;
        _serverMutationLock = serverMutationLock;
        _wipeEngine = wipeEngine;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            WipeQueuedOperation queuedOperation;
            try
            {
                queuedOperation = await _operationQueue.DequeueAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            await ExecuteOperationAsync(queuedOperation, stoppingToken);
        }
    }

    private async Task ExecuteOperationAsync(WipeQueuedOperation queuedOperation, CancellationToken stoppingToken)
    {
        var operationId = queuedOperation.OperationId;
        if (_operationStore.Get(operationId) is null)
        {
            _logger.LogWarning("Wipe operation {OperationId} was not found.", operationId);
            return;
        }

        try
        {
            await _serverMutationLock.ExecuteAsync(async () =>
            {
                MarkRunning(operationId);

                var serverStatus = await _serverControl.GetStatusAsync(stoppingToken);
                if (serverStatus.IsRunning)
                {
                    MarkFailed(operationId, ServerRunningError);
                    return;
                }

                var report = await _wipeEngine.ExecuteAsync(queuedOperation.Plan, stoppingToken);
                if (report.Succeeded)
                {
                    MarkSucceeded(operationId, report);
                }
                else
                {
                    MarkFailed(operationId, WipeFailedError, report);
                }
            }, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            MarkFailed(operationId, ShutdownFailureError);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Wipe operation {OperationId} failed.", operationId);
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

    private void MarkSucceeded(Guid operationId, WipeExecutionReport report)
    {
        var operation = GetOperation(operationId);
        _operationStore.Update(operation with
        {
            Status = OperationStatus.Succeeded,
            FinishedAt = _timeProvider.GetUtcNow(),
            Error = null,
            WipeResult = report
        });
    }

    private void MarkFailed(Guid operationId, string error, WipeExecutionReport? report = null)
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
            Error = error,
            WipeResult = report
        });
    }

    private Operation GetOperation(Guid operationId) =>
        _operationStore.Get(operationId)
        ?? throw new InvalidOperationException("The operation does not exist.");
}
