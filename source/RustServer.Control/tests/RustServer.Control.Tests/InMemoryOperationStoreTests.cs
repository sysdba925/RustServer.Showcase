using RustServer.Control.Domain.Operations;
using RustServer.Control.Infrastructure.Operations;

namespace RustServer.Control.Tests;

public sealed class InMemoryOperationStoreTests
{
    [Fact]
    public void Create_StoresPendingRustUpdateWithUnsetExecutionFields()
    {
        var createdAt = new DateTimeOffset(2026, 9, 27, 12, 0, 0, TimeSpan.Zero);
        var store = new InMemoryOperationStore(new FixedTimeProvider(createdAt));

        var operation = store.Create(OperationType.RustUpdate);

        Assert.NotEqual(Guid.Empty, operation.Id);
        Assert.Equal(OperationType.RustUpdate, operation.Type);
        Assert.Equal(OperationStatus.Pending, operation.Status);
        Assert.Equal(createdAt, operation.CreatedAt);
        Assert.Null(operation.StartedAt);
        Assert.Null(operation.FinishedAt);
        Assert.Null(operation.ExitCode);
        Assert.Null(operation.Error);
        Assert.Equal(operation, store.Get(operation.Id));
    }

    [Fact]
    public void Update_ReplacesTheStoredOperation()
    {
        var store = new InMemoryOperationStore(TimeProvider.System);
        var operation = store.Create(OperationType.RustUpdate);
        var completedAt = operation.CreatedAt.AddMinutes(1);
        var completedOperation = operation with
        {
            Status = OperationStatus.Succeeded,
            StartedAt = operation.CreatedAt,
            FinishedAt = completedAt,
            ExitCode = 0
        };

        store.Update(completedOperation);

        Assert.Equal(completedOperation, store.Get(operation.Id));
    }

    [Fact]
    public void Update_WhenOperationDoesNotExist_Throws()
    {
        var store = new InMemoryOperationStore(TimeProvider.System);
        var unknownOperation = new Operation(
            Guid.NewGuid(),
            OperationType.RustUpdate,
            OperationStatus.Pending,
            DateTimeOffset.UtcNow,
            null,
            null,
            null,
            null);

        Assert.Throws<InvalidOperationException>(() => store.Update(unknownOperation));
    }

    [Fact]
    public void TryCreateServerMutation_RejectsOxideWhenRustUpdateIsActive()
    {
        var store = new InMemoryOperationStore(TimeProvider.System);
        var rustUpdate = store.Create(OperationType.RustUpdate);

        var oxideUpdate = store.TryCreateServerMutation(OperationType.OxideUpdate);

        Assert.True(store.HasActiveServerMutation());
        Assert.Null(oxideUpdate);
        Assert.Equal(OperationStatus.Pending, store.Get(rustUpdate.Id)?.Status);
    }

    [Fact]
    public void TryCreateServerMutation_RejectsRustWhenOxideUpdateIsActive()
    {
        var store = new InMemoryOperationStore(TimeProvider.System);
        var oxideUpdate = store.Create(OperationType.OxideUpdate);

        var rustUpdate = store.TryCreateServerMutation(OperationType.RustUpdate);

        Assert.True(store.HasActiveServerMutation());
        Assert.Null(rustUpdate);
        Assert.Equal(OperationStatus.Pending, store.Get(oxideUpdate.Id)?.Status);
    }

    [Fact]
    public void TryCreateServerMutation_RejectsWipeWhenAnotherServerMutationIsActive()
    {
        var store = new InMemoryOperationStore(TimeProvider.System);
        store.Create(OperationType.RustUpdate);

        var wipe = store.TryCreateServerMutation(OperationType.Wipe);

        Assert.Null(wipe);
        Assert.True(store.HasActiveServerMutation());
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
