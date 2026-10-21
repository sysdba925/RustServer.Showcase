using System.Net;
using Xunit;

namespace RustServer.Plugins.Tests;

public sealed class HealthEndpointTests(PluginApplicationFactory factory)
    : IClassFixture<PluginApplicationFactory>
{
    [Fact]
    public async Task GetHealth_WhenServiceIsRunning_ReturnsOk()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
