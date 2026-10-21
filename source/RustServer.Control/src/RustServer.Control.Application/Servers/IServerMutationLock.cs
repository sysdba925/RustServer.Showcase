namespace RustServer.Control.Application.Servers;

/// <summary>
/// Serializes mutations to the single Rust server instance.
/// </summary>
public interface IServerMutationLock
{
    Task ExecuteAsync(
        Func<Task> operation,
        CancellationToken cancellationToken = default);

    Task<T> ExecuteAsync<T>(
        Func<Task<T>> operation,
        CancellationToken cancellationToken = default);
}
