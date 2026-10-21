using Microsoft.Extensions.Logging.Abstractions;
using RustServer.Updater.Api.Operations;

namespace RustServer.Updater.Tests;

public sealed class OperationManagerTests
{
    [Fact]
    public async Task CreateAsync_CreatesPendingOperationWithRequestedMetadata()
    {
        var manager = CreateManager(new InMemoryOperationStore());

        var operation = await manager.CreateAsync(
            new CreateOperationRequest(" rustserver ", OperationType.Update, "v2.0.0", "v1.0.0"),
            CancellationToken.None);

        Assert.NotEqual(Guid.Empty, operation.Id);
        Assert.Equal("rustserver", operation.ApplicationName);
        Assert.Equal(OperationType.Update, operation.Type);
        Assert.Equal(OperationStatus.Pending, operation.Status);
        Assert.Equal("Created", operation.Step);
        Assert.Equal("v2.0.0", operation.TargetVersion);
        Assert.Equal("v1.0.0", operation.PreviousVersion);
        Assert.NotEqual(default, operation.CreatedAt);
        Assert.Null(operation.StartedAt);
        Assert.Null(operation.CompletedAt);
        Assert.Null(operation.Error);
    }

    [Fact]
    public async Task GetAsync_ReturnsCreatedOperation()
    {
        var manager = CreateManager(new InMemoryOperationStore());
        var created = await CreateAsync(manager, "api");

        var found = await manager.GetAsync(created.Id, CancellationToken.None);

        Assert.Equal(created, found);
    }

    [Fact]
    public async Task Lifecycle_AllowsOnlyDefinedTransitions()
    {
        var manager = CreateManager(new InMemoryOperationStore());
        var created = await CreateAsync(manager, "api");

        var running = await manager.StartAsync(created.Id, CancellationToken.None);
        var stepped = await manager.UpdateStepAsync(created.Id, "Switching release", CancellationToken.None);
        var succeeded = await manager.CompleteAsync(created.Id, CancellationToken.None);

        Assert.Equal(OperationStatus.Running, running.Status);
        Assert.NotNull(running.StartedAt);
        Assert.Equal("Switching release", stepped.Step);
        Assert.Equal(OperationStatus.Succeeded, succeeded.Status);
        Assert.NotNull(succeeded.CompletedAt);
        await Assert.ThrowsAsync<OperationStateTransitionException>(
            () => manager.StartAsync(created.Id, CancellationToken.None));
        await Assert.ThrowsAsync<OperationStateTransitionException>(
            () => manager.UpdateStepAsync(created.Id, "After completion", CancellationToken.None));
    }

    [Fact]
    public async Task ConcurrentCreateAsync_AllowsIndependentApplications()
    {
        var manager = CreateManager(new InMemoryOperationStore());

        var operations = await Task.WhenAll(Enumerable.Range(0, 24)
            .Select(index => CreateAsync(manager, $"application-{index}")));

        Assert.Equal(24, operations.Select(operation => operation.Id).Distinct().Count());
        Assert.All(operations, operation => Assert.Equal(OperationStatus.Pending, operation.Status));
    }

    [Fact]
    public async Task ConcurrentCreateAsync_RejectsConflictingOperationForSameApplication()
    {
        var manager = CreateManager(new InMemoryOperationStore());

        var results = await Task.WhenAll(Enumerable.Range(0, 12).Select(async _ =>
        {
            try
            {
                return await CreateAsync(manager, "api");
            }
            catch (OperationConflictException)
            {
                return null;
            }
        }));

        Assert.Single(results, operation => operation is not null);
        Assert.Equal(11, results.Count(operation => operation is null));
    }

    [Fact]
    public async Task StateChange_WhenPersistenceFails_DoesNotReturnOrExposeChangedState()
    {
        var store = new InMemoryOperationStore();
        var manager = CreateManager(store);
        var created = await CreateAsync(manager, "api");
        store.FailSaves = true;

        await Assert.ThrowsAsync<IOException>(() => manager.StartAsync(created.Id, CancellationToken.None));

        var persisted = await store.GetAsync(created.Id, CancellationToken.None);
        Assert.NotNull(persisted);
        Assert.Equal(OperationStatus.Pending, persisted.Status);
    }

    [Fact]
    public async Task ExecuteAsync_UsesOperationCancellationRatherThanRequestCancellation()
    {
        var manager = CreateManager(new InMemoryOperationStore());
        var created = await CreateAsync(manager, "api");
        using var requestAborted = new CancellationTokenSource();
        var workerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishWorker = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var execution = manager.ExecuteAsync(
            created.Id,
            async cancellationToken =>
            {
                workerStarted.SetResult();
                await finishWorker.Task.WaitAsync(cancellationToken);
            },
            CancellationToken.None);

        await workerStarted.Task;
        requestAborted.Cancel();
        Assert.False(execution.IsCompleted);

        finishWorker.SetResult();
        var completed = await execution;

        Assert.Equal(OperationStatus.Succeeded, completed.Status);
    }

    [Fact]
    public async Task FailedAndInterruptedOperations_RemainInHistory()
    {
        var store = new InMemoryOperationStore();
        var manager = CreateManager(store);
        var failed = await CreateAsync(manager, "failed-app");
        var interrupted = await CreateAsync(manager, "interrupted-app");

        await manager.FailAsync(failed.Id, "archive validation failed", CancellationToken.None);
        await manager.InterruptAsync(interrupted.Id, "updater stopped", CancellationToken.None);

        var history = await manager.GetRecentAsync(10, CancellationToken.None);

        Assert.Contains(history, operation => operation.Id == failed.Id && operation.Status == OperationStatus.Failed);
        Assert.Contains(history, operation => operation.Id == interrupted.Id && operation.Status == OperationStatus.Interrupted);
    }

    [Fact]
    public async Task SetResultAsync_PersistsAStagedPackageBeforeCompletion()
    {
        var store = new InMemoryOperationStore();
        var manager = CreateManager(store);
        var operation = await CreateAsync(manager, "api");
        await manager.StartAsync(operation.Id, CancellationToken.None);
        var result = new PackagePreparationResult("api", "v1.0.0-dev.1", "/state/staging/api/v1.0.0-dev.1", new string('A', 64), DateTimeOffset.UtcNow);

        await manager.SetResultAsync(operation.Id, result, CancellationToken.None);
        var completed = await manager.CompleteAsync(operation.Id, CancellationToken.None);

        Assert.Equal(result, completed.Result);
    }

    [Fact]
    public async Task CompleteAsync_RejectsPrepareWithoutDurableResult()
    {
        var manager = CreateManager(new InMemoryOperationStore());
        var operation = await manager.CreateAsync(
            new CreateOperationRequest("api", OperationType.Prepare, "v1.0.0-dev.1"),
            CancellationToken.None);
        await manager.StartAsync(operation.Id, CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() => manager.CompleteAsync(operation.Id, CancellationToken.None));
    }

    private static OperationManager CreateManager(IOperationStore store) =>
        new(store, NullLogger<OperationManager>.Instance);

    private static Task<Operation> CreateAsync(IOperationManager manager, string applicationName) =>
        manager.CreateAsync(new CreateOperationRequest(applicationName, OperationType.Install), CancellationToken.None);
}

internal sealed class InMemoryOperationStore : IOperationStore
{
    private readonly object _syncRoot = new();
    private readonly Dictionary<Guid, Operation> _operations = [];

    public bool FailSaves { get; set; }

    public Task CreateAsync(Operation operation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_syncRoot)
        {
            if (!_operations.TryAdd(operation.Id, operation))
            {
                throw new InvalidOperationException("Operation already exists.");
            }
        }

        return Task.CompletedTask;
    }

    public Task SaveAsync(Operation operation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (FailSaves)
        {
            throw new IOException("The operation record could not be persisted.");
        }

        lock (_syncRoot)
        {
            if (!_operations.ContainsKey(operation.Id))
            {
                throw new KeyNotFoundException();
            }

            _operations[operation.Id] = operation;
        }

        return Task.CompletedTask;
    }

    public Task<Operation?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_syncRoot)
        {
            return Task.FromResult(_operations.GetValueOrDefault(id));
        }
    }

    public Task<IReadOnlyCollection<Operation>> GetRecentAsync(int limit, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_syncRoot)
        {
            return Task.FromResult<IReadOnlyCollection<Operation>>(_operations.Values
                .OrderByDescending(operation => operation.CreatedAt)
                .Take(limit)
                .ToArray());
        }
    }

    public Task<IReadOnlyCollection<Operation>> GetIncompleteAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_syncRoot)
        {
            return Task.FromResult<IReadOnlyCollection<Operation>>(_operations.Values
                .Where(operation => operation.Status is OperationStatus.Pending or OperationStatus.Running)
                .ToArray());
        }
    }
}
