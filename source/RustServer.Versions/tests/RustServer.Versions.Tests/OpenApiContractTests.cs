using RustServer.Versions.ApiContractGenerator;
using System.Text.Json.Nodes;

namespace RustServer.Versions.Tests;

public sealed class OpenApiContractTests(RustVersionsApiFactory factory) : IClassFixture<RustVersionsApiFactory>
{
    [Fact]
    public async Task CommittedArtifacts_MatchTheRunningApi()
    {
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
            if (File.Exists(Path.Combine(directory.FullName, "RustServer.Versions.sln")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Could not locate the RustServer.Versions repository root.");
    }

    private static string NormalizeNewlines(string value) => value.Replace("\r\n", "\n", StringComparison.Ordinal);
}
