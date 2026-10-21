using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using RustServer.Control.Application.StartupTracking;
using RustServer.Control.Application.Processes;
using RustServer.Control.Application.Servers;
using RustServer.Control.Domain.Servers;
using RustServer.Control.Domain.StartupTracking;
using RustServer.Control.Infrastructure.Servers;
using RustServer.Control.Infrastructure.StartupTracking;

namespace RustServer.Control.Tests;

public sealed class StartupTrackingApiTests
{
    [Fact]
    public async Task GetSchema_ReturnsExactFixedPresentationSchema()
    {
        using var factory = new StartupApiFactory(new FixedStartupTrackingService(
            new StartupSnapshot(null, false, PendingMilestones)));
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/server/startup/schema");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        var root = document.RootElement;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(1, root.GetProperty("version").GetInt32());
        var milestones = root.GetProperty("milestones");
        Assert.Equal(JsonValueKind.Array, milestones.ValueKind);
        Assert.Equal(9, milestones.GetArrayLength());
        Assert.Equal(
            [
                "engine",
                "mono",
                "bootstrap",
                "scene",
                "oxide",
                "steamInitialized",
                "serverStartup",
                "bootstrapCompleted",
                "steamConnected"
            ],
            milestones.EnumerateArray().Select(milestone => milestone.GetProperty("id").GetString()));
        Assert.Equal(
            [
                "Unity Engine",
                "Mono Runtime",
                "Bootstrap",
                "Scene Loading",
                "Oxide Core",
                "Steam Server Initialized",
                "Server Startup Complete",
                "Bootstrap Completed",
                "Steam Server Connected"
            ],
            milestones.EnumerateArray().Select(milestone => milestone.GetProperty("label").GetString()));
    }

    [Fact]
    public async Task GetSnapshot_ReturnsCamelCaseCompactPositionalState()
    {
        var states = new[]
        {
            StartupMilestoneState.Reached,
            StartupMilestoneState.Reached,
            StartupMilestoneState.Reached,
            StartupMilestoneState.Reached,
            StartupMilestoneState.Skipped,
            StartupMilestoneState.Reached,
            StartupMilestoneState.Pending,
            StartupMilestoneState.Pending,
            StartupMilestoneState.Pending
        };
        using var factory = new StartupApiFactory(new FixedStartupTrackingService(
            new StartupSnapshot("stp_opaque-session", false, states)));
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/server/startup");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        var root = document.RootElement;
        var milestones = root.GetProperty("milestones");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("stp_opaque-session", root.GetProperty("sessionId").GetString());
        Assert.False(root.GetProperty("started").GetBoolean());
        Assert.Equal(9, milestones.GetArrayLength());
        Assert.Equal(
            ["reached", "reached", "reached", "reached", "skipped", "reached", "pending", "pending", "pending"],
            milestones.EnumerateArray().Select(state => state.GetString()));
        Assert.All(
            milestones.EnumerateArray(),
            state => Assert.Contains(state.GetString(), new[] { "pending", "reached", "skipped" }));
        Assert.False(root.TryGetProperty("containerId", out _));
        Assert.False(root.TryGetProperty("timestamps", out _));
    }

    [Fact]
    public async Task GetSnapshot_WhenServerIsStopped_ReturnsNormalNullableSessionSnapshot()
    {
        using var factory = new StartupApiFactory(new FixedStartupTrackingService(
            new StartupSnapshot(null, false, PendingMilestones)));
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/server/startup");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        var root = document.RootElement;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("sessionId").ValueKind);
        Assert.False(root.GetProperty("started").GetBoolean());
        Assert.Equal(
            Enumerable.Repeat("pending", 9),
            root.GetProperty("milestones").EnumerateArray().Select(state => state.GetString()));
    }

    [Fact]
    public async Task OpenApi_ConstrainsStartupMilestonesToTheThreePublishedStringValues()
    {
        using var factory = new StartupApiFactory(new FixedStartupTrackingService(
            new StartupSnapshot(null, false, PendingMilestones)));
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/openapi.json");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        var stateSchema = document.RootElement
            .GetProperty("components")
            .GetProperty("schemas")
            .GetProperty("StartupSnapshotResponse")
            .GetProperty("properties")
            .GetProperty("milestones")
            .GetProperty("items");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("string", stateSchema.GetProperty("type").GetString());
        Assert.Equal(
            ["pending", "reached", "skipped"],
            stateSchema.GetProperty("enum").EnumerateArray().Select(value => value.GetString()));
    }

    [Fact]
    public async Task GetSnapshot_MapsConfigurationFailureToSanitizedInternalServerError()
    {
        using var factory = new StartupApiFactory(new ThrowingStartupTrackingService(
            new RustServerControlConfigurationException("Configuration value 'RustServerControl:RustServerComposePath' is required.")));
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/server/startup");
        var payload = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Contains("Rust server control is not configured", payload, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetSnapshot_MapsDockerFailureToSanitizedBadGateway()
    {
        using var factory = new StartupApiFactory(new ThrowingStartupTrackingService(
            new StartupTrackingException("log read")));
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/server/startup");
        var payload = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Contains("Unable to determine Rust server startup progress", payload, StringComparison.Ordinal);
        Assert.DoesNotContain("log read", payload, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetSnapshot_MapsProcessStartFailureToSanitizedBadGateway()
    {
        const string sensitiveDetail = "C:\\sensitive\\docker.exe";
        using var factory = new StartupApiFactory(new ThrowingStartupTrackingService(
            new ProcessExecutionException("docker", new InvalidOperationException(sensitiveDetail))));
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/server/startup");
        var payload = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Contains("Unable to start Docker", payload, StringComparison.Ordinal);
        Assert.DoesNotContain(sensitiveDetail, payload, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetStatus_RemainsTheExistingLifecycleAndReadinessContract()
    {
        using var factory = new StartupApiFactory(
            new FixedStartupTrackingService(new StartupSnapshot(null, false, PendingMilestones)),
            new FixedServerControl(new ServerStatus(IsRunning: true, IsReady: false)));
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/server/status");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        var properties = document.RootElement.EnumerateObject().Select(property => property.Name).Order();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(["isReady", "isRunning"], properties);
    }

    private static readonly StartupMilestoneState[] PendingMilestones =
        Enumerable.Repeat(StartupMilestoneState.Pending, 9).ToArray();

    private sealed class StartupApiFactory : WebApplicationFactory<Program>
    {
        private readonly IStartupTrackingService _startupTrackingService;
        private readonly IServerControl? _serverControl;

        public StartupApiFactory(
            IStartupTrackingService startupTrackingService,
            IServerControl? serverControl = null)
        {
            _startupTrackingService = startupTrackingService;
            _serverControl = serverControl;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(Environments.Production);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IStartupTrackingService>();
                services.AddSingleton(_startupTrackingService);

                if (_serverControl is not null)
                {
                    services.RemoveAll<IServerControl>();
                    services.AddSingleton(_serverControl);
                }
            });
        }
    }

    private sealed class FixedStartupTrackingService(StartupSnapshot snapshot) : IStartupTrackingService
    {
        public StartupTrackingSchema GetSchema() => StartupTrackingSchema.Version1;

        public Task<StartupSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(snapshot);
    }

    private sealed class ThrowingStartupTrackingService(Exception exception) : IStartupTrackingService
    {
        public StartupTrackingSchema GetSchema() => StartupTrackingSchema.Version1;

        public Task<StartupSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default) =>
            Task.FromException<StartupSnapshot>(exception);
    }

    private sealed class FixedServerControl(ServerStatus status) : IServerControl
    {
        public Task<ServerStatus> GetStatusAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(status);

        public Task<ServerStatus> StartAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(status);

        public Task<ServerStatus> StopAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(status);

        public Task<ServerStatus> RestartAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(status);
    }
}
