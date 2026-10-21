using System.Threading.Channels;

namespace RustServer.Plugins.Features.PluginManagement.RuntimeChanges;

/// <summary>
/// Publishes debounced Runtime-change invalidations to currently connected clients.
/// Notifications are intentionally transient: a client must read Runtime Diff again
/// to obtain the authoritative state.
/// </summary>
public sealed class RuntimeChangeNotifier : IDisposable
{
    private static readonly TimeSpan DebounceInterval = TimeSpan.FromMilliseconds(250);
    private readonly object sync = new();
    private readonly Dictionary<long, Channel<bool>> subscribers = [];
    private readonly Timer debounceTimer;
    private long nextSubscriberId;
    private bool disposed;

    public RuntimeChangeNotifier()
    {
        debounceTimer = new Timer(static state => ((RuntimeChangeNotifier) state!).Publish(), this,
            Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    public RuntimeChangeSubscription Subscribe()
    {
        lock (sync)
        {
            ThrowIfDisposed();

            var subscriptionId = ++nextSubscriberId;
            var channel = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false
            });
            subscribers.Add(subscriptionId, channel);

            return new RuntimeChangeSubscription(channel.Reader, () => Unsubscribe(subscriptionId));
        }
    }

    public void NotifyRuntimeFilesMayHaveChanged()
    {
        lock (sync)
        {
            if (disposed)
            {
                return;
            }

            debounceTimer.Change(DebounceInterval, Timeout.InfiniteTimeSpan);
        }
    }

    public void Dispose()
    {
        lock (sync)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            debounceTimer.Dispose();
            foreach (var channel in subscribers.Values)
            {
                channel.Writer.TryComplete();
            }

            subscribers.Clear();
        }
    }

    private void Publish()
    {
        lock (sync)
        {
            if (disposed)
            {
                return;
            }

            foreach (var channel in subscribers.Values)
            {
                channel.Writer.TryWrite(true);
            }
        }
    }

    private void Unsubscribe(long subscriptionId)
    {
        lock (sync)
        {
            if (subscribers.Remove(subscriptionId, out var channel))
            {
                channel.Writer.TryComplete();
            }
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
    }
}

public sealed class RuntimeChangeSubscription : IDisposable
{
    private readonly ChannelReader<bool> reader;
    private readonly Action unsubscribe;
    private int disposed;

    internal RuntimeChangeSubscription(ChannelReader<bool> reader, Action unsubscribe)
    {
        this.reader = reader;
        this.unsubscribe = unsubscribe;
    }

    public async ValueTask WaitAsync(CancellationToken cancellationToken)
    {
        await reader.ReadAsync(cancellationToken);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) == 0)
        {
            unsubscribe();
        }
    }
}
