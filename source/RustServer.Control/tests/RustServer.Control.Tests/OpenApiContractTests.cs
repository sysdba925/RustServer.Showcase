using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;
using RustServer.Control.ApiContractGenerator;

namespace RustServer.Control.Tests;

public sealed class OpenApiContractTests
{
    [Fact]
    public async Task Health_ReturnsOk()
    {
        using var factory = new ProductionApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task WipeActionSchema_UsesThePublishedStringWireValues()
    {
        using var factory = new ProductionApiFactory();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/openapi.json");

        response.EnsureSuccessStatusCode();

        var document = JsonNode.Parse(await response.Content.ReadAsStringAsync());
        var wipeAction = document?["components"]?["schemas"]?["WipeAction"]?.AsObject();

        Assert.NotNull(wipeAction);
        Assert.Equal("string", wipeAction["type"]?.GetValue<string>());
        Assert.Equal(
            ["keep", "delete", "transform"],
            wipeAction["enum"]?.AsArray().Select(value => value?.GetValue<string>()));
    }

    [Fact]
    public async Task CommittedArtifacts_MatchTheProductionApi()
    {
        using var factory = new ProductionApiFactory();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/openapi.json");

        response.EnsureSuccessStatusCode();

        var generatedOpenApi = JsonNode.Parse(await response.Content.ReadAsStringAsync());
        Assert.NotNull(generatedOpenApi);

        var repositoryRoot = FindRepositoryRoot();
        var committedOpenApi = JsonNode.Parse(File.ReadAllText(Path.Combine(repositoryRoot, "openapi.json")));
        Assert.True(
            JsonNode.DeepEquals(committedOpenApi, generatedOpenApi),
            "openapi.json is out of date. Run the API contract generator.");

        var committedMarkdown = File.ReadAllText(Path.Combine(repositoryRoot, "docs", "api-contract.md"));
        Assert.Equal(
            NormalizeNewlines(OpenApiMarkdownRenderer.Render(generatedOpenApi)),
            NormalizeNewlines(committedMarkdown));
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "RustServer.Control.sln")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Could not locate the RustServer.Control repository root.");
    }

    private static string NormalizeNewlines(string value) => value.Replace("\r\n", "\n", StringComparison.Ordinal);

    private sealed class ProductionApiFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) =>
            builder.UseEnvironment(Environments.Production);
    }
}
