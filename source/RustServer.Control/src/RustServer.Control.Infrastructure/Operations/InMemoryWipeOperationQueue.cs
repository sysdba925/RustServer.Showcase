using System.Threading.Channels;
using RustServer.Control.Application.Operations;
using RustServer.Control.Domain.Wipe;

namespace RustServer.Control.Infrastructure.Operations;

/// <summary>
/// An in-process, single-reader queue for wipe operations.
/// </summary>
public sealed class InMemoryWipeOperationQueue : IWipeOperationQueue
{
    private readonly Channel<WipeQueuedOperation> _operations = Channel.CreateUnbounded<WipeQueuedOperation>(
        new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });

    public void Enqueue(Guid operationId, WipePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        if (!_operations.Writer.TryWrite(new WipeQueuedOperation(operationId, plan)))
        {
            throw new InvalidOperationException("Unable to queue the wipe operation.");
        }
    }

    public ValueTask<WipeQueuedOperation> DequeueAsync(CancellationToken cancellationToken = default) =>
        _operations.Reader.ReadAsync(cancellationToken);
}
