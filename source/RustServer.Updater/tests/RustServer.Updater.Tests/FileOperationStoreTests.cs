using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RustServer.Updater.Api.Deployments;
using RustServer.Updater.Api.Operations;

namespace RustServer.Updater.Tests;

public sealed class FileOperationStoreTests
{
    [Fact]
    public async Task SaveAsync_PersistsAndReloadsOperationFromANewStoreInstance()
    {
        using var directory = new TemporaryDirectory();
        var store = CreateStore(directory.Path);
        var operation = CreateOperation();

        await store.CreateAsync(operation, CancellationToken.None);
        var running = operation with
        {
            Status = OperationStatus.Running,
            Step = "Downloading",
            StartedAt = DateTimeOffset.UtcNow
        };
        await store.SaveAsync(running, CancellationToken.None);

        var reloaded = await CreateStore(directory.Path).GetAsync(operation.Id, CancellationToken.None);

        Assert.Equal(running, reloaded);
    }

    [Fact]
    public async Task GetRecentAsync_IgnoresCorruptRecordAndKeepsOtherOperationsAvailable()
    {
        using var directory = new TemporaryDirectory();
        var store = CreateStore(directory.Path);
        var operation = CreateOperation();
        await store.CreateAsync(operation, CancellationToken.None);
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "corrupted.json"), "{ invalid json");

        var operations = await CreateStore(directory.Path).GetRecentAsync(10, CancellationToken.None);

        Assert.Single(operations);
        Assert.Equal(operation, operations.Single());
    }

    [Fact]
    public async Task CreateAsync_PropagatesWriteFailure()
    {
        using var directory = new TemporaryDirectory();
        var filePath = Path.Combine(directory.Path, "not-a-directory");
        await File.WriteAllTextAsync(filePath, "occupied");
        var store = CreateStore(filePath);

        await Assert.ThrowsAsync<IOException>(() => store.CreateAsync(CreateOperation(), CancellationToken.None));
    }

    [Fact]
    public async Task RecoveryService_MarksRecordsLeftRunningByPreviousProcessAsInterrupted()
    {
        using var directory = new TemporaryDirectory();
        var firstManager = CreateManager(CreateStore(directory.Path));
        var running = await firstManager.CreateAsync(
            new CreateOperationRequest("api", OperationType.Update),
            CancellationToken.None);
        var pending = await firstManager.CreateAsync(
            new CreateOperationRequest("worker", OperationType.SelfUpdate),
            CancellationToken.None);
        await firstManager.StartAsync(running.Id, CancellationToken.None);

        var restartedManager = CreateManager(CreateStore(directory.Path));
        var recoveryService = new OperationRecoveryService(
            restartedManager,
            CreateStore(directory.Path),
            new NoTransactionReconciler(),
            NullLogger<OperationRecoveryService>.Instance);
        await recoveryService.StartAsync(CancellationToken.None);

        var recovered = await restartedManager.GetAsync(running.Id, CancellationToken.None);
        var recoveredPending = await restartedManager.GetAsync(pending.Id, CancellationToken.None);
        Assert.NotNull(recovered);
        Assert.NotNull(recoveredPending);
        Assert.Equal(OperationStatus.Interrupted, recovered.Status);
        Assert.Equal(OperationStatus.Interrupted, recoveredPending.Status);
        Assert.NotNull(recovered.CompletedAt);
        Assert.Contains("restarted", recovered.Error, StringComparison.OrdinalIgnoreCase);
    }

    private static FileOperationStore CreateStore(string storagePath) =>
        new(
            Options.Create(new OperationStoreOptions { StoragePath = storagePath }),
            NullLogger<FileOperationStore>.Instance);

    private static OperationManager CreateManager(IOperationStore store) =>
        new(store, NullLogger<OperationManager>.Instance);

    private static Operation CreateOperation() => new(
        Guid.NewGuid(),
        "api",
        OperationType.Update,
        OperationStatus.Pending,
        "Created",
        "v2.0.0",
        "v1.0.0",
        DateTimeOffset.UtcNow,
        null,
        null,
        null);

    private sealed class NoTransactionReconciler : IDeploymentOperationReconciler
    {
        public Task ReconcileIncompleteAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<bool> ReconcileAsync(Operation operation, CancellationToken cancellationToken) =>
            Task.FromResult(false);
    }
}

internal sealed class TemporaryDirectory : IDisposable
{
    public TemporaryDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "RustServer.Updater.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
