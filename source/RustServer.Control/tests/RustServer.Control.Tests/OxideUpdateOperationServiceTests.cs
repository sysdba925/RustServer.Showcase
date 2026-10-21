using RustServer.Control.Application.Operations;
using RustServer.Control.Application.Servers;
using RustServer.Control.Domain.Operations;
using RustServer.Control.Domain.Servers;
using RustServer.Control.Infrastructure.Operations;

namespace RustServer.Control.Tests;

public sealed class OxideUpdateOperationServiceTests
{
    [Fact]
    public async Task QueueAsync_WhenServerIsStopped_CreatesPendingOxideOperation()
    {
        var store = new InMemoryOperationStore(TimeProvider.System);
        var queue = new InMemoryOxideUpdateOperationQueue();
        var service = CreateService(store, queue, isRunning: false);

        var result = await service.QueueAsync();

        Assert.Equal(OxideUpdateOperationQueueStatus.Accepted, result.Status);
        var operation = Assert.IsType<Operation>(result.Operation);
        Assert.Equal(OperationType.OxideUpdate, operation.Type);
        Assert.Equal(OperationStatus.Pending, operation.Status);
        Assert.Equal(operation.Id, await queue.DequeueAsync());
    }

    [Fact]
    public async Task QueueAsync_WhenServerIsRunning_RejectsWithoutCreatingAnOperation()
    {
        var store = new InMemoryOperationStore(TimeProvider.System);
        var service = CreateService(store, new InMemoryOxideUpdateOperationQueue(), isRunning: true);

        var result = await service.QueueAsync();

        Assert.Equal(OxideUpdateOperationQueueStatus.ServerRunning, result.Status);
        Assert.Null(result.Operation);
        Assert.False(store.HasActiveServerMutation());
    }

    [Fact]
    public async Task QueueAsync_WhenOxideUpdateIsPending_RejectsDuplicateOperation()
    {
        var store = new InMemoryOperationStore(TimeProvider.System);
        store.Create(OperationType.OxideUpdate);
        var service = CreateService(store, new InMemoryOxideUpdateOperationQueue(), isRunning: false);

        var result = await service.QueueAsync();

        Assert.Equal(OxideUpdateOperationQueueStatus.OperationInProgress, result.Status);
        Assert.Null(result.Operation);
    }

    [Fact]
    public async Task QueueAsync_WhenRustUpdateIsPending_RejectsConflictingOperation()
    {
        var store = new InMemoryOperationStore(TimeProvider.System);
        store.Create(OperationType.RustUpdate);
        var service = CreateService(store, new InMemoryOxideUpdateOperationQueue(), isRunning: false);

        var result = await service.QueueAsync();

        Assert.Equal(OxideUpdateOperationQueueStatus.OperationInProgress, result.Status);
        Assert.Null(result.Operation);
    }

    private static OxideUpdateOperationService CreateService(
        IOperationStore operationStore,
        IOxideUpdateOperationQueue operationQueue,
        bool isRunning) =>
        new(
            operationStore,
            operationQueue,
            new FixedStatusServerControl(isRunning));

    private sealed class FixedStatusServerControl(bool isRunning) : IServerControl
    {
        private readonly ServerStatus _status = new(isRunning, IsReady: false);

        public Task<ServerStatus> GetStatusAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_status);

        public Task<ServerStatus> StartAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ServerStatus> StopAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ServerStatus> RestartAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
