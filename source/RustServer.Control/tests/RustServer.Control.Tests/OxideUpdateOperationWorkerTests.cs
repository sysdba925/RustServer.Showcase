using Microsoft.Extensions.Logging.Abstractions;
using RustServer.Control.Application.Operations;
using RustServer.Control.Application.Servers;
using RustServer.Control.Domain.Operations;
using RustServer.Control.Domain.Servers;
using RustServer.Control.Infrastructure.Operations;
using RustServer.Control.Infrastructure.Servers;

namespace RustServer.Control.Tests;

public sealed class OxideUpdateOperationWorkerTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Worker_TransitionsPendingOperationThroughRunningToSucceeded()
    {
        var store = new InMemoryOperationStore(TimeProvider.System);
        var queue = new InMemoryOxideUpdateOperationQueue();
        var updater = new BlockingOxideUpdater(new OxideUpdateResult(Succeeded: true, ExitCode: 0));
        var worker = CreateWorker(store, queue, new FixedStatusServerControl(isRunning: false), updater);
        var operation = store.Create(OperationType.OxideUpdate);

        await worker.StartAsync(CancellationToken.None);
        try
        {
            queue.Enqueue(operation.Id);
            await updater.ExecutionStarted.Task.WaitAsync(TestTimeout);

            var runningOperation = Assert.IsType<Operation>(store.Get(operation.Id));
            Assert.Equal(OperationStatus.Running, runningOperation.Status);
            Assert.NotNull(runningOperation.StartedAt);

            updater.AllowExecutionToComplete.SetResult();

            var completedOperation = await WaitForTerminalOperationAsync(store, operation.Id);
            Assert.Equal(OperationStatus.Succeeded, completedOperation.Status);
            Assert.Equal(0, completedOperation.ExitCode);
        }
        finally
        {
            updater.AllowExecutionToComplete.TrySetResult();
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Worker_WhenUpdaterReturnsFailure_TransitionsOperationToFailed()
    {
        var store = new InMemoryOperationStore(TimeProvider.System);
        var queue = new InMemoryOxideUpdateOperationQueue();
        var worker = CreateWorker(
            store,
            queue,
            new FixedStatusServerControl(isRunning: false),
            new ImmediateOxideUpdater(new OxideUpdateResult(Succeeded: false, ExitCode: 17)));
        var operation = store.Create(OperationType.OxideUpdate);

        await worker.StartAsync(CancellationToken.None);
        try
        {
            queue.Enqueue(operation.Id);

            var completedOperation = await WaitForTerminalOperationAsync(store, operation.Id);
            Assert.Equal(OperationStatus.Failed, completedOperation.Status);
            Assert.Equal(17, completedOperation.ExitCode);
            Assert.Equal("Oxide update failed.", completedOperation.Error);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Worker_WhenUpdaterThrows_TransitionsOperationToSanitizedFailure()
    {
        var store = new InMemoryOperationStore(TimeProvider.System);
        var queue = new InMemoryOxideUpdateOperationQueue();
        var worker = CreateWorker(
            store,
            queue,
            new FixedStatusServerControl(isRunning: false),
            new ThrowingOxideUpdater(new InvalidOperationException(@"C:\sensitive\oxide-compose.yml")));
        var operation = store.Create(OperationType.OxideUpdate);

        await worker.StartAsync(CancellationToken.None);
        try
        {
            queue.Enqueue(operation.Id);

            var completedOperation = await WaitForTerminalOperationAsync(store, operation.Id);
            Assert.Equal(OperationStatus.Failed, completedOperation.Status);
            Assert.Null(completedOperation.ExitCode);
            Assert.Equal("The Oxide update could not be completed.", completedOperation.Error);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Worker_WhenServerIsRunning_DoesNotRunOxideUpdater()
    {
        var store = new InMemoryOperationStore(TimeProvider.System);
        var queue = new InMemoryOxideUpdateOperationQueue();
        var updater = new ImmediateOxideUpdater(new OxideUpdateResult(Succeeded: true, ExitCode: 0));
        var worker = CreateWorker(store, queue, new FixedStatusServerControl(isRunning: true), updater);
        var operation = store.Create(OperationType.OxideUpdate);

        await worker.StartAsync(CancellationToken.None);
        try
        {
            queue.Enqueue(operation.Id);

            var completedOperation = await WaitForTerminalOperationAsync(store, operation.Id);
            Assert.Equal(OperationStatus.Failed, completedOperation.Status);
            Assert.Equal("Rust server must be stopped before Oxide can be updated.", completedOperation.Error);
            Assert.Equal(0, updater.ExecutionCount);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    private static OxideUpdateOperationWorker CreateWorker(
        IOperationStore operationStore,
        IOxideUpdateOperationQueue operationQueue,
        IServerControl serverControl,
        IOxideUpdater updater) =>
        new(
            operationStore,
            operationQueue,
            serverControl,
            new ServerMutationLock(),
            updater,
            TimeProvider.System,
            NullLogger<OxideUpdateOperationWorker>.Instance);

    private static async Task<Operation> WaitForTerminalOperationAsync(
        IOperationStore operationStore,
        Guid operationId)
    {
        using var timeout = new CancellationTokenSource(TestTimeout);

        while (true)
        {
            var operation = Assert.IsType<Operation>(operationStore.Get(operationId));
            if (operation.Status is OperationStatus.Succeeded or OperationStatus.Failed)
            {
                return operation;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(10), timeout.Token);
        }
    }

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

    private sealed class BlockingOxideUpdater(OxideUpdateResult result) : IOxideUpdater
    {
        public TaskCompletionSource ExecutionStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource AllowExecutionToComplete { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<OxideUpdateResult> UpdateAsync(CancellationToken cancellationToken = default)
        {
            ExecutionStarted.SetResult();
            await AllowExecutionToComplete.Task.WaitAsync(cancellationToken);
            return result;
        }
    }

    private sealed class ImmediateOxideUpdater(OxideUpdateResult result) : IOxideUpdater
    {
        private int _executionCount;

        public int ExecutionCount => Volatile.Read(ref _executionCount);

        public Task<OxideUpdateResult> UpdateAsync(CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _executionCount);
            return Task.FromResult(result);
        }
    }

    private sealed class ThrowingOxideUpdater(Exception exception) : IOxideUpdater
    {
        public Task<OxideUpdateResult> UpdateAsync(CancellationToken cancellationToken = default) =>
            Task.FromException<OxideUpdateResult>(exception);
    }
}
