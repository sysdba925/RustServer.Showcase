using RustServer.Control.Application.Operations;
using RustServer.Control.Domain.Operations;

namespace RustServer.Control.Infrastructure.Operations;

/// <summary>
/// Stores operation state for the lifetime of the current Control service process.
/// </summary>
public sealed class InMemoryOperationStore : IOperationStore
{
    private readonly object _sync = new();
    private readonly Dictionary<Guid, Operation> _operations = [];
    private readonly TimeProvider _timeProvider;

    public InMemoryOperationStore(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        _timeProvider = timeProvider;
    }

    public Operation Create(OperationType type)
    {
        lock (_sync)
        {
            return CreateOperation(type);
        }
    }

    public Operation? TryCreateServerMutation(OperationType type)
    {
        if (!type.IsServerMutation())
        {
            throw new ArgumentException("The operation type is not a server mutation.", nameof(type));
        }

        lock (_sync)
        {
            return HasActiveServerMutationOperation()
                ? null
                : CreateOperation(type);
        }
    }

    public Operation? Get(Guid id)
    {
        lock (_sync)
        {
            return _operations.GetValueOrDefault(id);
        }
    }

    public bool HasActiveServerMutation()
    {
        lock (_sync)
        {
            return HasActiveServerMutationOperation();
        }
    }

    public void Update(Operation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        lock (_sync)
        {
            if (!_operations.ContainsKey(operation.Id))
            {
                throw new InvalidOperationException("The operation does not exist.");
            }

            _operations[operation.Id] = operation;
        }
    }

    private Operation CreateOperation(OperationType type)
    {
        var operation = new Operation(
            Id: Guid.NewGuid(),
            Type: type,
            Status: OperationStatus.Pending,
            CreatedAt: _timeProvider.GetUtcNow(),
            StartedAt: null,
            FinishedAt: null,
            ExitCode: null,
            Error: null);

        _operations.Add(operation.Id, operation);

        return operation;
    }

    private bool HasActiveServerMutationOperation() =>
        _operations.Values.Any(operation =>
            operation.Type.IsServerMutation()
            && operation.Status is OperationStatus.Pending or OperationStatus.Running);
}
