using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using RustServer.Control.Application.Servers;
using RustServer.Control.Domain.Servers;
using RustServer.Control.Infrastructure.Servers;

namespace RustServer.Control.Tests;

public sealed class ServerConfigurationApiTests
{
    [Fact]
    public async Task GetConfiguration_ReturnsDesiredAndEffectiveValuesThatMayDiffer()
    {
        var desired = CreateConfiguration(seed: 67890);
        var effective = CreateConfiguration(seed: 12345);
        using var factory = new ConfigurationApiFactory(new InMemoryServerConfigurationStore(desired, effective));
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/server/configuration");
        var configuration = await response.Content.ReadFromJsonAsync<ServerConfigurationResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var value = Assert.IsType<ServerConfigurationResponse>(configuration);
        Assert.Equal(desired, value.Desired);
        Assert.Equal(effective, value.Effective);
    }

    [Fact]
    public async Task PutConfiguration_UpdatesDesiredWithoutChangingEffectiveConfiguration()
    {
        var originalDesired = CreateConfiguration(seed: 12345);
        var effective = CreateConfiguration(seed: 12345);
        var configurationStore = new InMemoryServerConfigurationStore(originalDesired, effective);
        using var factory = new ConfigurationApiFactory(configurationStore);
        using var client = factory.CreateClient();
        var request = new UpdateServerConfigurationRequest(
            "New Rust Server",
            "New description",
            20,
            10,
            600,
            67890,
            4200);

        var response = await client.PutAsJsonAsync("/api/server/configuration", request);
        var configuration = await response.Content.ReadFromJsonAsync<ServerConfigurationResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var value = Assert.IsType<ServerConfigurationResponse>(configuration);
        Assert.Equal(new ServerConfiguration(
            "New Rust Server", "New description", 20, 10, 600, 67890, 4200), value.Desired);
        Assert.Equal(effective, value.Effective);
        Assert.Equal(value, ServerConfigurationResponse.From(configurationStore.Get()));
    }

    [Fact]
    public async Task PutConfiguration_RejectsInvalidWorldSize()
    {
        using var factory = new ConfigurationApiFactory(new InMemoryServerConfigurationStore(CreateConfiguration()));
        using var client = factory.CreateClient();
        var request = new UpdateServerConfigurationRequest(
            "Rust Server", string.Empty, 20, 10, 600, 1, 999);

        var response = await client.PutAsJsonAsync("/api/server/configuration", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static ServerConfiguration CreateConfiguration(int seed = 12345) => new(
        "Rust Server",
        "Description",
        50,
        30,
        600,
        seed,
        3500);

    private sealed class ConfigurationApiFactory(IServerConfigurationStore configurationStore)
        : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(Environments.Development);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IServerConfigurationStore>();
                services.AddSingleton(configurationStore);
            });
        }
    }
}
