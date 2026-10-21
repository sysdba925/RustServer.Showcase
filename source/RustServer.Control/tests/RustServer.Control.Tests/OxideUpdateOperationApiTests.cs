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

public sealed class OxideUpdateOperationApiTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task PostOxideUpdate_ReturnsPendingOperationThatCanBeQueried()
    {
        var oxideUpdater = new ImmediateOxideUpdater(new OxideUpdateResult(Succeeded: true, ExitCode: 0));
        using var factory = new OperationApiFactory(
            new FixedStatusServerControl(isRunning: false),
            new ImmediateRustServerUpdater(),
            oxideUpdater);
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/api/updates/oxide", content: null);
        var acceptedOperation = await response.Content.ReadFromJsonAsync<OperationAcceptedResponse>();

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.NotNull(acceptedOperation);
        Assert.Equal("Pending", acceptedOperation.Status);
        Assert.Equal($"/api/operations/{acceptedOperation.Id}", response.Headers.Location?.OriginalString);

        var completedOperation = await WaitForTerminalOperationAsync(client, acceptedOperation.Id);
        Assert.Equal("OxideUpdate", completedOperation.Type);
        Assert.Equal("Succeeded", completedOperation.Status);
        Assert.Equal(0, completedOperation.ExitCode);
        Assert.Null(completedOperation.Error);
    }

    [Fact]
    public async Task PostOxideUpdate_WhenServerIsRunning_ReturnsConflictWithoutQueuingAnOperation()
    {
        var oxideUpdater = new ImmediateOxideUpdater(new OxideUpdateResult(Succeeded: true, ExitCode: 0));
        using var factory = new OperationApiFactory(
            new FixedStatusServerControl(isRunning: true),
            new ImmediateRustServerUpdater(),
            oxideUpdater);
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/api/updates/oxide", content: null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(0, oxideUpdater.ExecutionCount);
    }

    [Fact]
    public async Task PostOxideUpdate_WhenOxideUpdateIsActive_RejectsDuplicateOperation()
    {
        var oxideUpdater = new BlockingOxideUpdater();
        using var factory = new OperationApiFactory(
            new FixedStatusServerControl(isRunning: false),
            new ImmediateRustServerUpdater(),
            oxideUpdater);
        using var client = factory.CreateClient();

        var firstResponse = await client.PostAsync("/api/updates/oxide", content: null);
        var firstOperation = await firstResponse.Content.ReadFromJsonAsync<OperationAcceptedResponse>();
        Assert.Equal(HttpStatusCode.Accepted, firstResponse.StatusCode);
        Assert.NotNull(firstOperation);
        await oxideUpdater.ExecutionStarted.Task.WaitAsync(TestTimeout);

        var secondResponse = await client.PostAsync("/api/updates/oxide", content: null);

        Assert.Equal(HttpStatusCode.Conflict, secondResponse.StatusCode);

        oxideUpdater.AllowExecutionToComplete.SetResult();
        var completedOperation = await WaitForTerminalOperationAsync(client, firstOperation.Id);
        Assert.Equal("Succeeded", completedOperation.Status);
    }

    [Fact]
    public async Task PostOxideUpdate_WhenRustUpdateIsActive_ReturnsConflict()
    {
        var rustUpdater = new BlockingRustServerUpdater();
        using var factory = new OperationApiFactory(
            new FixedStatusServerControl(isRunning: false),
            rustUpdater,
            new ImmediateOxideUpdater(new OxideUpdateResult(Succeeded: true, ExitCode: 0)));
        using var client = factory.CreateClient();

        var rustResponse = await client.PostAsync("/api/server/update", content: null);
        var rustOperation = await rustResponse.Content.ReadFromJsonAsync<OperationAcceptedResponse>();
        Assert.Equal(HttpStatusCode.Accepted, rustResponse.StatusCode);
        Assert.NotNull(rustOperation);
        await rustUpdater.ExecutionStarted.Task.WaitAsync(TestTimeout);

        var oxideResponse = await client.PostAsync("/api/updates/oxide", content: null);

        Assert.Equal(HttpStatusCode.Conflict, oxideResponse.StatusCode);

        rustUpdater.AllowExecutionToComplete.SetResult();
        var completedOperation = await WaitForTerminalOperationAsync(client, rustOperation.Id);
        Assert.Equal("Succeeded", completedOperation.Status);
    }

    [Theory]
    [InlineData("/api/server/update")]
    [InlineData("/api/server/start")]
    [InlineData("/api/server/restart")]
    public async Task WhenOxideUpdateIsActive_ConflictingServerMutationReturnsConflict(string path)
    {
        var oxideUpdater = new BlockingOxideUpdater();
        using var factory = new OperationApiFactory(
            new FixedStatusServerControl(isRunning: false),
            new ImmediateRustServerUpdater(),
            oxideUpdater);
        using var client = factory.CreateClient();

        var oxideResponse = await client.PostAsync("/api/updates/oxide", content: null);
        var oxideOperation = await oxideResponse.Content.ReadFromJsonAsync<OperationAcceptedResponse>();
        Assert.Equal(HttpStatusCode.Accepted, oxideResponse.StatusCode);
        Assert.NotNull(oxideOperation);
        await oxideUpdater.ExecutionStarted.Task.WaitAsync(TestTimeout);

        var conflictingResponse = await client.PostAsync(path, content: null);

        Assert.Equal(HttpStatusCode.Conflict, conflictingResponse.StatusCode);

        oxideUpdater.AllowExecutionToComplete.SetResult();
        var completedOperation = await WaitForTerminalOperationAsync(client, oxideOperation.Id);
        Assert.Equal("Succeeded", completedOperation.Status);
    }

    private static async Task<OperationResponse> WaitForTerminalOperationAsync(HttpClient client, Guid operationId)
    {
        using var timeout = new CancellationTokenSource(TestTimeout);

        while (true)
        {
            var response = await client.GetAsync($"/api/operations/{operationId}", timeout.Token);
            var operation = await response.Content.ReadFromJsonAsync<OperationResponse>(timeout.Token);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var currentOperation = Assert.IsType<OperationResponse>(operation);
            if (currentOperation.Status is "Succeeded" or "Failed")
            {
                return currentOperation;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(10), timeout.Token);
        }
    }

    private sealed class OperationApiFactory : WebApplicationFactory<Program>
    {
        private readonly IServerControl _serverControl;
        private readonly IRustServerUpdater _rustUpdater;
        private readonly IOxideUpdater _oxideUpdater;

        public OperationApiFactory(
            IServerControl serverControl,
            IRustServerUpdater rustUpdater,
            IOxideUpdater oxideUpdater)
        {
            _serverControl = serverControl;
            _rustUpdater = rustUpdater;
            _oxideUpdater = oxideUpdater;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(Environments.Development);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IServerControl>();
                services.RemoveAll<IRustServerUpdater>();
                services.RemoveAll<IOxideUpdater>();
                services.AddSingleton(_serverControl);
                services.AddSingleton(_rustUpdater);
                services.AddSingleton(_oxideUpdater);
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

    private sealed class ImmediateRustServerUpdater : IRustServerUpdater
    {
        public Task<RustServerUpdateResult> UpdateAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new RustServerUpdateResult(Succeeded: true, ExitCode: 0));
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

    private sealed class BlockingOxideUpdater : IOxideUpdater
    {
        public TaskCompletionSource ExecutionStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource AllowExecutionToComplete { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<OxideUpdateResult> UpdateAsync(CancellationToken cancellationToken = default)
        {
            ExecutionStarted.SetResult();
            await AllowExecutionToComplete.Task.WaitAsync(cancellationToken);
            return new OxideUpdateResult(Succeeded: true, ExitCode: 0);
        }
    }
}
