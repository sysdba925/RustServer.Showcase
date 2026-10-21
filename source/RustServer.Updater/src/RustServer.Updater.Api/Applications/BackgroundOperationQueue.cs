using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace RustServer.Updater.Api.Applications;

/// <summary>Single-reader background queue for accepted operation work.</summary>
public sealed class BackgroundOperationQueue(ILogger<BackgroundOperationQueue> logger)
    : BackgroundService, IBackgroundOperationQueue
{
    private readonly Channel<Func<CancellationToken, Task>> _queue = Channel.CreateUnbounded<Func<CancellationToken, Task>>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

    /// <inheritdoc />
    public void Enqueue(Func<CancellationToken, Task> work)
    {
        ArgumentNullException.ThrowIfNull(work);
        if (!_queue.Writer.TryWrite(work))
        {
            throw new InvalidOperationException("The operation queue is unavailable.");
        }
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var work in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await work(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // OperationManager records interruption before the host exits.
            }
            catch (Exception exception)
            {
                // OperationManager has already durably recorded the sanitized failure.
                logger.LogError(exception, "A queued updater operation ended unexpectedly.");
            }
        }
    }
}
