using RustServer.Control.Domain.Operations;

namespace RustServer.Control.Application.Operations;

/// <summary>
/// Stores the current state of long-running control-service operations.
/// </summary>
public interface IOperationStore
{
    Operation Create(OperationType type);

    /// <summary>
    /// Creates a server-mutating operation only when no server-mutating operation is pending or running.
    /// </summary>
    Operation? TryCreateServerMutation(OperationType type);

    Operation? Get(Guid id);

    bool HasActiveServerMutation();

    void Update(Operation operation);
}
