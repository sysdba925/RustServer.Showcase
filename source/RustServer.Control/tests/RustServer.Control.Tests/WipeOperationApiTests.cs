using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using RustServer.Control.Application.Servers;
using RustServer.Control.Application.Wipe;
using RustServer.Control.Domain.Servers;
using RustServer.Control.Domain.Wipe;
using RustServer.Control.Infrastructure.Servers;

namespace RustServer.Control.Tests;

public sealed class WipeOperationApiTests : IDisposable
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);
    private readonly string _dataRoot = Path.Combine(Path.GetTempPath(), $"RustServer.Control.WipeApiTests.{Guid.NewGuid():N}");

    [Fact]
    public async Task PostWipe_WhenServerIsRunning_ReturnsConflictWithoutCreatingAnOperation()
    {
        using var factory = new WipeApiFactory(new FixedStatusServerControl(isRunning: true), _dataRoot);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/server/wipe", new
        {
            world = "delete",
            blueprints = "delete"
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task PostWipe_RejectsNumericActionsBecauseThePublicContractUsesStrings()
    {
        using var factory = new WipeApiFactory(new FixedStatusServerControl(isRunning: false), _dataRoot);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/server/wipe", new
        {
            world = 1,
            blueprints = 0
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PostWipe_UsesExistingOperationsApiAndDoesNotAcceptClientFilesystemPaths()
    {
        var identityDirectory = Path.Combine(_dataRoot, "server", "unit-server");
        Directory.CreateDirectory(identityDirectory);
        var mapPath = Path.Combine(identityDirectory, "proceduralmap.3500.1.map");
        var blueprintPath = Path.Combine(identityDirectory, "player.blueprints.5.db");
        File.WriteAllText(mapPath, "map");
        File.WriteAllText(blueprintPath, "blueprints");
        var outsidePath = Path.Combine(_dataRoot, "outside.txt");
        File.WriteAllText(outsidePath, "keep");
        using var factory = new WipeApiFactory(new FixedStatusServerControl(isRunning: false), _dataRoot);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/server/wipe", new
        {
            world = "delete",
            blueprints = "keep",
            path = outsidePath
        });
        var accepted = await response.Content.ReadFromJsonAsync<OperationAcceptedResponse>();

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var operation = Assert.IsType<OperationAcceptedResponse>(accepted);
        Assert.Equal($"/api/operations/{operation.Id}", response.Headers.Location?.OriginalString);

        var completed = await WaitForTerminalOperationAsync(client, operation.Id);
        Assert.Equal("Wipe", completed.Type);
        Assert.Equal("Succeeded", completed.Status);
        var result = Assert.IsType<WipeOperationResultResponse>(completed.WipeResult);
        Assert.True(result.Succeeded);
        Assert.Equal("delete", result.Targets.Single(target => target.Target == "world").Action);
        Assert.Equal("deleted", result.Targets.Single(target => target.Target == "world").Outcome);
        Assert.Equal("keep", result.Targets.Single(target => target.Target == "blueprints").Action);
        Assert.Equal("kept", result.Targets.Single(target => target.Target == "blueprints").Outcome);
        Assert.False(File.Exists(mapPath));
        Assert.True(File.Exists(blueprintPath));
        Assert.True(File.Exists(outsidePath));
    }

    [Fact]
    public async Task PostWipe_WhenExecutionFails_ExposesFailedStateAndPathFreeReportThroughOperationsApi()
    {
        using var factory = new WipeApiFactory(
            new FixedStatusServerControl(isRunning: false),
            _dataRoot,
            new FailingWipeEngine());
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/server/wipe", new
        {
            world = "delete",
            blueprints = "keep"
        });
        var accepted = await response.Content.ReadFromJsonAsync<OperationAcceptedResponse>();

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var operation = Assert.IsType<OperationAcceptedResponse>(accepted);
        var completed = await WaitForTerminalOperationAsync(client, operation.Id);
        Assert.Equal("Failed", completed.Status);
        Assert.Equal("Wipe operation failed.", completed.Error);
        var report = Assert.IsType<WipeOperationResultResponse>(completed.WipeResult);
        Assert.False(report.Succeeded);
        Assert.Equal("failed", Assert.Single(report.Targets).Outcome);
        Assert.DoesNotContain(_dataRoot, completed.Error, StringComparison.Ordinal);
    }

    private static async Task<OperationResponse> WaitForTerminalOperationAsync(HttpClient client, Guid operationId)
    {
        using var timeout = new CancellationTokenSource(TestTimeout);
        while (true)
        {
            var response = await client.GetAsync($"/api/operations/{operationId}", timeout.Token);
            var operation = await response.Content.ReadFromJsonAsync<OperationResponse>(timeout.Token);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var current = Assert.IsType<OperationResponse>(operation);
            if (current.Status is "Succeeded" or "Failed")
            {
                return current;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(10), timeout.Token);
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_dataRoot))
        {
            Directory.Delete(_dataRoot, recursive: true);
        }
    }

    private sealed class WipeApiFactory(
        IServerControl serverControl,
        string dataRoot,
        IWipeEngine? wipeEngine = null)
        : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(Environments.Development);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IServerControl>();
                services.RemoveAll<RustServerControlOptions>();
                if (wipeEngine is not null)
                {
                    services.RemoveAll<IWipeEngine>();
                    services.AddSingleton(wipeEngine);
                }
                services.AddSingleton(serverControl);
                services.AddSingleton(new RustServerControlOptions
                {
                    RustServerComposePath = "unused-compose-path",
                    RustServerDataPath = dataRoot,
                    RustServerIdentity = "unit-server"
                });
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

    private sealed class FailingWipeEngine : IWipeEngine
    {
        public string? ValidatePlan(WipePlan plan) => null;

        public Task<WipeExecutionReport> ExecuteAsync(
            WipePlan plan,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new WipeExecutionReport(
                false,
                [new WipeTargetResult(
                    "world",
                    WipeAction.Delete,
                    WipeTargetOutcome.Failed,
                    0,
                    "The target could not be processed.")]));
    }
}
