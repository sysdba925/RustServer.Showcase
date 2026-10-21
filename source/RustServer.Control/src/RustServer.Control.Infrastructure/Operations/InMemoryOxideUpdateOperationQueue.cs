using System.Threading.Channels;
using RustServer.Control.Application.Operations;

namespace RustServer.Control.Infrastructure.Operations;

/// <summary>
/// An in-process, single-reader queue for Oxide update operations.
/// </summary>
public sealed class InMemoryOxideUpdateOperationQueue : IOxideUpdateOperationQueue
{
    private readonly Channel<Guid> _operationIds = Channel.CreateUnbounded<Guid>(
        new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });

    public void Enqueue(Guid operationId)
    {
        if (!_operationIds.Writer.TryWrite(operationId))
        {
            throw new InvalidOperationException("Unable to queue the Oxide update operation.");
        }
    }

    public ValueTask<Guid> DequeueAsync(CancellationToken cancellationToken = default) =>
        _operationIds.Reader.ReadAsync(cancellationToken);
}
