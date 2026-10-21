using RustServer.Control.Application.Servers;

namespace RustServer.Control.Infrastructure.Servers;

/// <summary>
/// Provides a single exclusivity boundary for Rust server mutations.
/// </summary>
public sealed class ServerMutationLock : IServerMutationLock
{
    private readonly SemaphoreSlim _lock = new(1, 1);

    public async Task ExecuteAsync(
        Func<Task> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        await _lock.WaitAsync(cancellationToken);
        try
        {
            await operation();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<T> ExecuteAsync<T>(
        Func<Task<T>> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        await _lock.WaitAsync(cancellationToken);
        try
        {
            return await operation();
        }
        finally
        {
            _lock.Release();
        }
    }
}
