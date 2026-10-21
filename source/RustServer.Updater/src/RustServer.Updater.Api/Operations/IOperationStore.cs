namespace RustServer.Updater.Api.Operations;

/// <summary>
/// Persists operation records independently of the operation lifecycle.
/// </summary>
public interface IOperationStore
{
    /// <summary>
    /// Creates a new durable operation record.
    /// </summary>
    Task CreateAsync(Operation operation, CancellationToken cancellationToken);

    /// <summary>
    /// Persists a replacement for an existing operation record.
    /// </summary>
    Task SaveAsync(Operation operation, CancellationToken cancellationToken);

    /// <summary>
    /// Reads one operation by identifier.
    /// </summary>
    Task<Operation?> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Reads the newest durable operation records, limited by the supplied count.
    /// </summary>
    Task<IReadOnlyCollection<Operation>> GetRecentAsync(int limit, CancellationToken cancellationToken);

    /// <summary>
    /// Reads all operations which were not terminal when the process stopped.
    /// </summary>
    Task<IReadOnlyCollection<Operation>> GetIncompleteAsync(CancellationToken cancellationToken);
}
