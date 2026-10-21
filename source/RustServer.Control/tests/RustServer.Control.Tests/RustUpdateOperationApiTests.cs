using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using RustServer.Control.Application.Servers;
using RustServer.Control.Domain.Servers;

namespace RustServer.Control.Tests;

public sealed class RustUpdateOperationApiTests
{
    [Fact]
    public async Task PostUpdate_ReturnsAcceptedOperationThatCanBeQueriedAndCompletesSuccessfully()
    {
        var updater = new ControlledRustServerUpdater(new RustServerUpdateResult(Succeeded: true, ExitCode: 0));
        using var factory = new OperationApiFactory(new FixedStatusServerControl(isRunning: false), updater);
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/api/server/update", content: null);
        var acceptedOperation = await response.Content.ReadFromJsonAsync<OperationAcceptedResponse>();

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.NotNull(acceptedOperation);
        Assert.NotEqual(Guid.Empty, acceptedOperation.Id);
        Assert.Contains(acceptedOperation.Status, new[] { "Pending", "Running" });
        Assert.Equal($"/api/operations/{acceptedOperation.Id}", response.Headers.Location?.OriginalString);

        await updater.ExecutionStarted.Task.WaitAsync(TestTimeout);

        var runningOperation = await GetOperationAsync(client, acceptedOperation.Id);
        Assert.Equal("RustUpdate", runningOperation.Type);
        Assert.Equal("Running", runningOperation.Status);
        Assert.NotNull(runningOperation.StartedAt);
        Assert.Null(runningOperation.FinishedAt);
        Assert.Null(runningOperation.ExitCode);
        Assert.Null(runningOperation.Error);

        updater.AllowExecutionToComplete.SetResult();

        var completedOperation = await WaitForTerminalOperationAsync(client, acceptedOperation.Id);
        Assert.Equal("Succeeded", completedOperation.Status);
        Assert.Equal(0, completedOperation.ExitCode);
        Assert.NotNull(completedOperation.FinishedAt);
        Assert.Null(completedOperation.Error);
    }

    [Fact]
    public async Task PostUpdate_WhenUpdaterReturnsNonZeroExitCode_CompletesOperationAsFailedAndRetainsExitCode()
    {
        var updater = new ControlledRustServerUpdater(new RustServerUpdateResult(Succeeded: false, ExitCode: 23));
        using var factory = new OperationApiFactory(new FixedStatusServerControl(isRunning: false), updater);
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/api/server/update", content: null);
        var acceptedOperation = await response.Content.ReadFromJsonAsync<OperationAcceptedResponse>();

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.NotNull(acceptedOperation);
        await updater.ExecutionStarted.Task.WaitAsync(TestTimeout);

        updater.AllowExecutionToComplete.SetResult();

        var completedOperation = await WaitForTerminalOperationAsync(client, acceptedOperation.Id);
        Assert.Equal("Failed", completedOperation.Status);
        Assert.Equal(23, completedOperation.ExitCode);
        Assert.Equal("Rust server update failed.", completedOperation.Error);
    }

    [Fact]
    public async Task PostUpdate_RequestCancellationAfterAcceptance_DoesNotCancelWorkerExecution()
    {
        var updater = new ControlledRustServerUpdater(new RustServerUpdateResult(Succeeded: true, ExitCode: 0));
        using var factory = new OperationApiFactory(new FixedStatusServerControl(isRunning: false), updater);
        using var client = factory.CreateClient();
        using var requestCancellation = new CancellationTokenSource();

        var response = await client.PostAsync("/api/server/update", content: null, requestCancellation.Token);
        var acceptedOperation = await response.Content.ReadFromJsonAsync<OperationAcceptedResponse>();
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.NotNull(acceptedOperation);
        await updater.ExecutionStarted.Task.WaitAsync(TestTimeout);

        requestCancellation.Cancel();

        Assert.False(updater.ExecutionCancellationToken.IsCancellationRequested);

        updater.AllowExecutionToComplete.SetResult();

        var completedOperation = await WaitForTerminalOperationAsync(client, acceptedOperation.Id);
        Assert.Equal("Succeeded", completedOperation.Status);
    }

    [Fact]
    public async Task GetOperation_WhenOperationDoesNotExist_ReturnsNotFound()
    {
        using var factory = new OperationApiFactory(
            new FixedStatusServerControl(isRunning: false),
            new ControlledRustServerUpdater(new RustServerUpdateResult(Succeeded: true, ExitCode: 0)));
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"/api/operations/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PostUpdate_WhenRustServerIsRunning_ReturnsConflictWithoutQueuingAnOperation()
    {
        var updater = new ControlledRustServerUpdater(new RustServerUpdateResult(Succeeded: true, ExitCode: 0));
        using var factory = new OperationApiFactory(new FixedStatusServerControl(isRunning: true), updater);
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/api/server/update", content: null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(0, updater.ExecutionCount);
    }

    [Fact]
    public async Task QueuedUpdate_WhenServerStartsBeforeWorkerGetsTheLock_FailsWithoutRunningSteamCmd()
    {
        var updater = new ControlledRustServerUpdater(new RustServerUpdateResult(Succeeded: true, ExitCode: 0));
        var serverControl = new ScriptedStatusServerControl(false, true);
        using var factory = new OperationApiFactory(serverControl, updater);
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/api/server/update", content: null);
        var acceptedOperation = await response.Content.ReadFromJsonAsync<OperationAcceptedResponse>();

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.NotNull(acceptedOperation);

        var completedOperation = await WaitForTerminalOperationAsync(client, acceptedOperation.Id);
        Assert.Equal("Failed", completedOperation.Status);
        Assert.Equal("Rust server must be stopped before it can be updated.", completedOperation.Error);
        Assert.Null(completedOperation.ExitCode);
        Assert.Equal(0, updater.ExecutionCount);
    }

    [Fact]
    public async Task PostUpdate_WhenWorkerThrows_ExposesOnlyASanitizedOperationError()
    {
        const string internalMessage = "C:\\sensitive\\steamcmd-compose.yml";
        using var factory = new OperationApiFactory(
            new FixedStatusServerControl(isRunning: false),
            new ThrowingRustServerUpdater(new InvalidOperationException(internalMessage)));
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/api/server/update", content: null);
        var acceptedOperation = await response.Content.ReadFromJsonAsync<OperationAcceptedResponse>();

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.NotNull(acceptedOperation);

        var completedOperation = await WaitForTerminalOperationAsync(client, acceptedOperation.Id);
        Assert.Equal("Failed", completedOperation.Status);
        Assert.Equal("The Rust server update could not be completed.", completedOperation.Error);
        Assert.DoesNotContain(internalMessage, completedOperation.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PostUpdate_WhenRustUpdateIsPendingOrRunning_ReturnsConflictWithoutQueuingASecondUpdate()
    {
        var updater = new ControlledRustServerUpdater(new RustServerUpdateResult(Succeeded: true, ExitCode: 0));
        using var factory = new OperationApiFactory(new FixedStatusServerControl(isRunning: false), updater);
        using var client = factory.CreateClient();

        var firstResponse = await client.PostAsync("/api/server/update", content: null);
        var firstOperation = await firstResponse.Content.ReadFromJsonAsync<OperationAcceptedResponse>();
        Assert.Equal(HttpStatusCode.Accepted, firstResponse.StatusCode);
        Assert.NotNull(firstOperation);
        await updater.ExecutionStarted.Task.WaitAsync(TestTimeout);

        var secondResponse = await client.PostAsync("/api/server/update", content: null);
        var problemDetails = await secondResponse.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Conflict, secondResponse.StatusCode);
        Assert.Contains("Operation already in progress", problemDetails, StringComparison.Ordinal);
        Assert.Equal(1, updater.ExecutionCount);
        Assert.Equal(1, updater.MaximumConcurrentExecutionCount);

        updater.AllowExecutionToComplete.SetResult();

        var firstCompletedOperation = await WaitForTerminalOperationAsync(client, firstOperation.Id);
        Assert.Equal("Succeeded", firstCompletedOperation.Status);
        Assert.Equal(1, updater.MaximumConcurrentExecutionCount);
    }

    [Theory]
    [InlineData("/api/server/start")]
    [InlineData("/api/server/stop")]
    [InlineData("/api/server/restart")]
    public async Task LifecycleMutation_WhenRustUpdateIsRunning_ReturnsConflictWithoutWaiting(string path)
    {
        var updater = new ControlledRustServerUpdater(new RustServerUpdateResult(Succeeded: true, ExitCode: 0));
        using var factory = new OperationApiFactory(new FixedStatusServerControl(isRunning: false), updater);
        using var client = factory.CreateClient();

        var updateResponse = await client.PostAsync("/api/server/update", content: null);
        var acceptedOperation = await updateResponse.Content.ReadFromJsonAsync<OperationAcceptedResponse>();
        Assert.Equal(HttpStatusCode.Accepted, updateResponse.StatusCode);
        Assert.NotNull(acceptedOperation);
        await updater.ExecutionStarted.Task.WaitAsync(TestTimeout);

        var lifecycleResponse = await client.PostAsync(path, content: null);
        var problemDetails = await lifecycleResponse.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Conflict, lifecycleResponse.StatusCode);
        Assert.Contains("Server mutation in progress", problemDetails, StringComparison.Ordinal);

        updater.AllowExecutionToComplete.SetResult();
        var completedOperation = await WaitForTerminalOperationAsync(client, acceptedOperation.Id);
        Assert.Equal("Succeeded", completedOperation.Status);
    }

    [Fact]
    public async Task GetStatus_WhenRustUpdateIsRunning_RemainsAvailable()
    {
        var updater = new ControlledRustServerUpdater(new RustServerUpdateResult(Succeeded: true, ExitCode: 0));
        using var factory = new OperationApiFactory(new FixedStatusServerControl(isRunning: false), updater);
        using var client = factory.CreateClient();

        var updateResponse = await client.PostAsync("/api/server/update", content: null);
        var acceptedOperation = await updateResponse.Content.ReadFromJsonAsync<OperationAcceptedResponse>();
        Assert.Equal(HttpStatusCode.Accepted, updateResponse.StatusCode);
        Assert.NotNull(acceptedOperation);
        await updater.ExecutionStarted.Task.WaitAsync(TestTimeout);

        var statusResponse = await client.GetAsync("/api/server/status");
        var serverStatus = await statusResponse.Content.ReadFromJsonAsync<ServerStatus>();

        Assert.Equal(HttpStatusCode.OK, statusResponse.StatusCode);
        Assert.Equal(new ServerStatus(IsRunning: false, IsReady: false), serverStatus);

        updater.AllowExecutionToComplete.SetResult();
        var completedOperation = await WaitForTerminalOperationAsync(client, acceptedOperation.Id);
        Assert.Equal("Succeeded", completedOperation.Status);
    }

    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);

    private static async Task<OperationResponse> GetOperationAsync(HttpClient client, Guid operationId)
    {
        var response = await client.GetAsync($"/api/operations/{operationId}");
        var operation = await response.Content.ReadFromJsonAsync<OperationResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return Assert.IsType<OperationResponse>(operation);
    }

    private static async Task<OperationResponse> WaitForTerminalOperationAsync(HttpClient client, Guid operationId)
    {
        using var timeout = new CancellationTokenSource(TestTimeout);

        while (true)
        {
            var operation = await GetOperationAsync(client, operationId);
            if (operation.Status is "Succeeded" or "Failed")
            {
                return operation;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(10), timeout.Token);
        }
    }

    private sealed class OperationApiFactory : WebApplicationFactory<Program>
    {
        private readonly IServerControl _serverControl;
        private readonly IRustServerUpdater _updater;

        public OperationApiFactory(IServerControl serverControl, IRustServerUpdater updater)
        {
            _serverControl = serverControl;
            _updater = updater;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(Environments.Development);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IServerControl>();
                services.RemoveAll<IRustServerUpdater>();
                services.AddSingleton(_serverControl);
                services.AddSingleton(_updater);
            });
        }
    }

    private sealed class FixedStatusServerControl(bool isRunning) : IServerControl
    {
        private readonly ServerStatus _status = new(isRunning, IsReady: false);

        public Task<ServerStatus> GetStatusAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_status);

        public Task<ServerStatus> StartAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_status);

        public Task<ServerStatus> StopAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_status);

        public Task<ServerStatus> RestartAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_status);
    }

    private sealed class ScriptedStatusServerControl(params bool[] isRunning) : IServerControl
    {
        private readonly Queue<bool> _statuses = new(isRunning);

        public Task<ServerStatus> GetStatusAsync(CancellationToken cancellationToken = default)
        {
            var currentStatus = _statuses.Count > 1 ? _statuses.Dequeue() : _statuses.Peek();
            return Task.FromResult(new ServerStatus(currentStatus, IsReady: false));
        }

        public Task<ServerStatus> StartAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ServerStatus> StopAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ServerStatus> RestartAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class ControlledRustServerUpdater(RustServerUpdateResult result) : IRustServerUpdater
    {
        private int _executionCount;
        private int _concurrentExecutionCount;
        private int _maximumConcurrentExecutionCount;

        public TaskCompletionSource ExecutionStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource AllowExecutionToComplete { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public CancellationToken ExecutionCancellationToken { get; private set; }

        public int ExecutionCount => Volatile.Read(ref _executionCount);

        public int MaximumConcurrentExecutionCount => Volatile.Read(ref _maximumConcurrentExecutionCount);

        public async Task<RustServerUpdateResult> UpdateAsync(CancellationToken cancellationToken = default)
        {
            ExecutionCancellationToken = cancellationToken;
            Interlocked.Increment(ref _executionCount);
            var concurrentExecutionCount = Interlocked.Increment(ref _concurrentExecutionCount);
            UpdateMaximumConcurrentExecutionCount(concurrentExecutionCount);
            ExecutionStarted.TrySetResult();

            try
            {
                await AllowExecutionToComplete.Task.WaitAsync(cancellationToken);
                return result;
            }
            finally
            {
                Interlocked.Decrement(ref _concurrentExecutionCount);
            }
        }

        private void UpdateMaximumConcurrentExecutionCount(int concurrentExecutionCount)
        {
            while (true)
            {
                var currentMaximum = Volatile.Read(ref _maximumConcurrentExecutionCount);
                if (concurrentExecutionCount <= currentMaximum
                    || Interlocked.CompareExchange(
                        ref _maximumConcurrentExecutionCount,
                        concurrentExecutionCount,
                        currentMaximum) == currentMaximum)
                {
                    return;
                }
            }
        }
    }

    private sealed class ThrowingRustServerUpdater(Exception exception) : IRustServerUpdater
    {
        public Task<RustServerUpdateResult> UpdateAsync(CancellationToken cancellationToken = default) =>
            Task.FromException<RustServerUpdateResult>(exception);
    }
}
