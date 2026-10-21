using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using RustServer.Updater.Api.Operations;

namespace RustServer.Updater.Tests;

public sealed class OperationsEndpointTests(UpdaterWebApplicationFactory factory)
    : IClassFixture<UpdaterWebApplicationFactory>
{
    [Fact]
    public async Task GetOperation_ReturnsPersistedOperation()
    {
        using var directory = new TemporaryDirectory();
        using var configuredFactory = CreateFactory(directory.Path);
        using var client = configuredFactory.CreateClient();
        var manager = configuredFactory.Services.GetRequiredService<IOperationManager>();
        var created = await manager.CreateAsync(
            new CreateOperationRequest("api", OperationType.Install, "v1.0.0"),
            CancellationToken.None);

        using var response = await client.GetAsync($"/api/operations/{created.Id}");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(created.Id, document.RootElement.GetProperty("id").GetGuid());
        Assert.Equal("api", document.RootElement.GetProperty("applicationName").GetString());
        Assert.Equal("Install", document.RootElement.GetProperty("type").GetString());
        Assert.Equal("Pending", document.RootElement.GetProperty("status").GetString());
        Assert.Equal("Created", document.RootElement.GetProperty("step").GetString());
    }

    [Fact]
    public async Task GetOperation_ReturnsNotFoundForUnknownOperation()
    {
        using var directory = new TemporaryDirectory();
        using var configuredFactory = CreateFactory(directory.Path);
        using var client = configuredFactory.CreateClient();

        using var response = await client.GetAsync($"/api/operations/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetOperations_ReturnsNewestRecordsWithinLimit()
    {
        using var directory = new TemporaryDirectory();
        using var configuredFactory = CreateFactory(directory.Path);
        using var client = configuredFactory.CreateClient();
        var manager = configuredFactory.Services.GetRequiredService<IOperationManager>();
        var first = await manager.CreateAsync(
            new CreateOperationRequest("first", OperationType.Install),
            CancellationToken.None);
        await Task.Delay(TimeSpan.FromMilliseconds(10));
        var second = await manager.CreateAsync(
            new CreateOperationRequest("second", OperationType.Update),
            CancellationToken.None);

        using var response = await client.GetAsync("/api/operations?limit=1");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(document.RootElement.EnumerateArray());
        Assert.Equal(second.Id, document.RootElement[0].GetProperty("id").GetGuid());
        Assert.NotEqual(first.Id, second.Id);
    }

    private WebApplicationFactory<Program> CreateFactory(string storagePath) =>
        factory.WithWebHostBuilder(builder => builder.UseSetting("Operations:StoragePath", storagePath));
}
