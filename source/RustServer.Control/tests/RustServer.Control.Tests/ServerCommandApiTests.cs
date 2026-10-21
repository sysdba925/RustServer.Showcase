using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using RustServer.Control.Application.Servers;
using RustServer.Control.Domain.Servers;
using RustServer.Control.Infrastructure.Servers;

namespace RustServer.Control.Tests;

public sealed class ServerCommandApiTests
{
    [Fact]
    public async Task GetStatus_ReturnsRunningAndReadyFlags()
    {
        using var factory = new StatusApiFactory(new ServerStatus(IsRunning: true, IsReady: false));
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/server/status");
        var status = await response.Content.ReadFromJsonAsync<ServerStatus>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new ServerStatus(IsRunning: true, IsReady: false), status);
    }

    [Fact]
    public async Task OpenApi_ExposesExpandedServerStatusModel()
    {
        using var factory = new StatusApiFactory(new ServerStatus(IsRunning: false, IsReady: false));
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/openapi.json");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        var properties = document.RootElement
            .GetProperty("components")
            .GetProperty("schemas")
            .GetProperty("ServerStatus")
            .GetProperty("properties");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(properties.TryGetProperty("isRunning", out _));
        Assert.True(properties.TryGetProperty("isReady", out _));
    }

    [Fact]
    public async Task Development_MapsCommandEndpointAndForwardsValidCommand()
    {
        using var factory = new CommandApiFactory(Environments.Development);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/server/command",
            new ServerCommandRequest("status"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(["status"], factory.CommandClient.Commands);
        var result = await response.Content.ReadFromJsonAsync<ServerCommandResponse>();
        Assert.NotNull(result);
        Assert.Equal("ok", result.ResponseMessage);
    }

    [Fact]
    public async Task Production_DoesNotMapCommandEndpoint()
    {
        using var factory = new CommandApiFactory(Environments.Production);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/server/command",
            new ServerCommandRequest("status"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(factory.CommandClient.Commands);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Development_RejectsEmptyCommand(string command)
    {
        using var factory = new CommandApiFactory(Environments.Development);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/server/command",
            new ServerCommandRequest(command));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(factory.CommandClient.Commands);
    }

    [Fact]
    public async Task Development_RejectsNullCommand()
    {
        using var factory = new CommandApiFactory(Environments.Development);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/server/command",
            new ServerCommandRequest(null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(factory.CommandClient.Commands);
    }

    [Fact]
    public async Task Development_RejectsOversizedCommand()
    {
        using var factory = new CommandApiFactory(Environments.Development);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/server/command",
            new ServerCommandRequest(new string('x', 4_097)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(factory.CommandClient.Commands);
    }

    [Fact]
    public async Task Development_ReturnsBoundedCommandResponse()
    {
        using var factory = new CommandApiFactory(Environments.Development);
        factory.CommandClient.Result = new RconCommandResult(new string('x', 20_000));
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/server/command",
            new ServerCommandRequest("status"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<ServerCommandResponse>();
        Assert.NotNull(result);
        Assert.Equal(16_384, result.ResponseMessage.Length);
    }

    [Fact]
    public async Task Development_ConvertsRconServiceExceptionToSanitizedProblemDetails()
    {
        const string password = "secret/with?uri#characters";
        using var factory = new CommandApiFactory(Environments.Development);
        factory.CommandClient.Exception = new RconClientException(
            $"Connection failed for {password}",
            new InvalidOperationException());
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/server/command",
            new ServerCommandRequest("status"));
        var problemDetails = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Contains("Rust server command failed", problemDetails, StringComparison.Ordinal);
        Assert.DoesNotContain(password, problemDetails, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Development_ReportsRconServiceConfigurationAsProblemDetails()
    {
        const string password = "secret/that-must-not-be-returned";
        using var factory = new CommandApiFactory(Environments.Development);
        factory.CommandClient.Exception = new RconClientConfigurationException(
            $"Configuration value '{password}' is required.");
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/server/command",
            new ServerCommandRequest("status"));
        var problemDetails = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.DoesNotContain(password, problemDetails, StringComparison.Ordinal);
    }

    private sealed class CommandApiFactory : WebApplicationFactory<Program>
    {
        private readonly string _environmentName;

        public CommandApiFactory(string environmentName)
        {
            _environmentName = environmentName;
        }

        public RecordingServerCommandClient CommandClient { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(_environmentName);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IRconClient>();
                services.AddSingleton<IRconClient>(CommandClient);
            });
        }
    }

    private sealed class StatusApiFactory : WebApplicationFactory<Program>
    {
        private readonly ServerStatus _status;

        public StatusApiFactory(ServerStatus status)
        {
            _status = status;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(Environments.Development);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IServerControl>();
                services.AddSingleton<IServerControl>(new FixedStatusServerControl(_status));
            });
        }
    }

    private sealed class FixedStatusServerControl : IServerControl
    {
        private readonly ServerStatus _status;

        public FixedStatusServerControl(ServerStatus status)
        {
            _status = status;
        }

        public Task<ServerStatus> GetStatusAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_status);

        public Task<ServerStatus> StartAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_status);

        public Task<ServerStatus> StopAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_status);

        public Task<ServerStatus> RestartAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_status);
    }

    private sealed class RecordingServerCommandClient : IRconClient
    {
        public List<string> Commands { get; } = [];

        public RconCommandResult Result { get; set; } = new("ok");

        public Exception? Exception { get; set; }

        public Task<RconCommandResult> ExecuteCommandAsync(
            string command,
            CancellationToken cancellationToken = default)
        {
            Commands.Add(command);

            return Exception is null
                ? Task.FromResult(Result)
                : Task.FromException<RconCommandResult>(Exception);
        }

        public Task<RconConnectionState> GetConnectionStatusAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(RconConnectionState.Connected);
    }
}
