using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using System.Net;

namespace RustServer.PluginCatalog.Api.Tests;

public sealed class HealthEndpointTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task GetHealth_ReturnsOk()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetHealthReady_ReturnsServiceUnavailableWhenDependenciesAreUnavailable()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task GetHealthReady_ReturnsOkWhenDatabaseAndMediaRootAreUsable()
    {
        using var readinessFactory = new CatalogApiFactory();
        using var client = readinessFactory.CreateClient();

        using var response = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task DevelopmentOpenApi_DescribesProblemDetailsCode()
    {
        using var developmentFactory = factory.WithWebHostBuilder(builder =>
            builder.UseEnvironment("Development"));
        using var client = developmentFactory.CreateClient();

        using var response = await client.GetAsync("/openapi/v1.json");
        var document = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"code\"", document, StringComparison.Ordinal);
        Assert.Contains("\"sourceDescription\"", document, StringComparison.Ordinal);
        Assert.Contains("\"currency\"", document, StringComparison.Ordinal);
        Assert.Contains("/api/tags", document, StringComparison.Ordinal);
        Assert.Contains("/api/folders", document, StringComparison.Ordinal);
        Assert.Contains("\"tagId\"", document, StringComparison.Ordinal);
        Assert.Contains("\"folderId\"", document, StringComparison.Ordinal);
    }
}
