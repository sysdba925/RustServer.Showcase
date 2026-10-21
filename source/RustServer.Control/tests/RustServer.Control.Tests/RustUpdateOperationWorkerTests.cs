using Microsoft.Extensions.Logging.Abstractions;
using RustServer.Control.Application.Operations;
using RustServer.Control.Application.Servers;
using RustServer.Control.Domain.Operations;
using RustServer.Control.Domain.Servers;
using RustServer.Control.Infrastructure.Operations;
using RustServer.Control.Infrastructure.Servers;

namespace RustServer.Control.Tests;

public sealed class RustUpdateOperationWorkerTests
{
    [Fact]
    public async Task RunningUpdate_PreventsLifecycleMutationUntilSteamCmdFinishes()
    {
        var mutationLock = new ServerMutationLock();
        var operationStore = new InMemoryOperationStore(TimeProvider.System);
        var operationQueue = new InMemoryRustUpdateOperationQueue();
        var serverControl = new LockAwareServerControl(mutationLock);
        var updater = new BlockingRustServerUpdater();
        var worker = new RustUpdateOperationWorker(
            operationStore,
            operationQueue,
            serverControl,
            mutationLock,
            updater,
            TimeProvider.System,
            NullLogger<RustUpdateOperationWorker>.Instance);

        await worker.StartAsync(CancellationToken.None);
        try
        {
            var operation = operationStore.Create(OperationType.RustUpdate);
            operationQueue.Enqueue(operation.Id);
            await updater.ExecutionStarted.Task.WaitAsync(TestTimeout);

            var startTask = serverControl.StartAsync();

            Assert.False(serverControl.StartEntered.Task.IsCompleted);

            updater.AllowExecutionToComplete.SetResult();
            await startTask.WaitAsync(TestTimeout);

            Assert.True(serverControl.StartEntered.Task.IsCompleted);
        }
        finally
        {
            updater.AllowExecutionToComplete.TrySetResult();
            await worker.StopAsync(CancellationToken.None);
        }
    }

    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);

    private sealed class LockAwareServerControl(IServerMutationLock mutationLock) : IServerControl
    {
        public TaskCompletionSource StartEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<ServerStatus> GetStatusAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new ServerStatus(IsRunning: false, IsReady: false));

        public Task<ServerStatus> StartAsync(CancellationToken cancellationToken = default) =>
            mutationLock.ExecuteAsync(() =>
            {
                StartEntered.SetResult();
                return Task.FromResult(new ServerStatus(IsRunning: true, IsReady: false));
            }, cancellationToken);

        public Task<ServerStatus> StopAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ServerStatus> RestartAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class BlockingRustServerUpdater : IRustServerUpdater
    {
        public TaskCompletionSource ExecutionStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource AllowExecutionToComplete { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<RustServerUpdateResult> UpdateAsync(CancellationToken cancellationToken = default)
        {
            ExecutionStarted.SetResult();
            await AllowExecutionToComplete.Task.WaitAsync(cancellationToken);

            return new RustServerUpdateResult(Succeeded: true, ExitCode: 0);
        }
    }
}
