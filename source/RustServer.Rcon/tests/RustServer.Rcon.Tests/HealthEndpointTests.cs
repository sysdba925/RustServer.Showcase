using Microsoft.AspNetCore.Mvc.Testing;

namespace RustServer.Rcon.Tests;

public sealed class HealthEndpointTests : IClassFixture<RconGrpcApplicationFactory>
{
    private readonly RconGrpcApplicationFactory _applicationFactory;

    public HealthEndpointTests(RconGrpcApplicationFactory applicationFactory)
    {
        _applicationFactory = applicationFactory;
    }

    [Fact]
    public async Task GetHealth_WhenServiceIsRunning_ReturnsOk()
    {
        using var client = _applicationFactory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetOpenApiDocument_IncludesHealthEndpoint()
    {
        using var client = _applicationFactory.CreateClient();

        var document = await client.GetStringAsync("/openapi.json");

        Assert.Contains("\"/health\"", document, StringComparison.Ordinal);
    }
}

public sealed class RconGrpcApplicationFactory : WebApplicationFactory<Program>, IDisposable
{
    private readonly string? _originalRconUrl = Environment.GetEnvironmentVariable("RUSTSERVER_RCON_URL");
    private readonly string? _originalPassword = Environment.GetEnvironmentVariable("RustServerRcon__Password");

    public RconGrpcApplicationFactory()
    {
        Environment.SetEnvironmentVariable("RUSTSERVER_RCON_URL", "ws://127.0.0.1:28016");
        Environment.SetEnvironmentVariable("RustServerRcon__Password", "test-password");
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Environment.SetEnvironmentVariable("RUSTSERVER_RCON_URL", _originalRconUrl);
            Environment.SetEnvironmentVariable("RustServerRcon__Password", _originalPassword);
        }

        base.Dispose(disposing);
    }
}
