using RustServer.Control.Application.Servers;
using RustServer.Control.Infrastructure.Servers;

namespace RustServer.Control.Tests;

public sealed class ServerMutationLockTests
{
    [Fact]
    public async Task ExecuteAsync_SerializesOperationsUsingTheSameLock()
    {
        IServerMutationLock mutationLock = new ServerMutationLock();
        var firstOperationStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowFirstOperationToComplete = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondOperationStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var firstOperation = mutationLock.ExecuteAsync(async () =>
        {
            firstOperationStarted.SetResult();
            await allowFirstOperationToComplete.Task;
        });
        await firstOperationStarted.Task;

        var secondOperation = mutationLock.ExecuteAsync(() =>
        {
            secondOperationStarted.SetResult();
            return Task.CompletedTask;
        });

        Assert.False(secondOperationStarted.Task.IsCompleted);

        allowFirstOperationToComplete.SetResult();
        await Task.WhenAll(firstOperation, secondOperation);

        Assert.True(secondOperationStarted.Task.IsCompleted);
    }
}
