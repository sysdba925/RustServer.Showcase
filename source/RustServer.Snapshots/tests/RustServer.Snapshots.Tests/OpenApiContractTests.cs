using RustServer.Snapshots.ApiContractGenerator;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RustServer.Snapshots.Tests;

public sealed class SnapshotsApiTests(SnapshotsApiFactory factory) : IClassFixture<SnapshotsApiFactory>
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

    [Fact]
    public async Task Health_ReturnsOk()
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task SnapshotWorkflow_EnforcesServerStateAndPersistsSnapshot()
    {
        using var client = factory.CreateClient();

        factory.ControlServer.IsRunning = true;
        using (var response = await client.PostAsync("/api/snapshots", CreateSnapshotRequest("consistent")))
        {
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            await AssertProblemTitleAsync(response, "ServerMustBeStopped");
        }

        // Fast is accepted while running without sending any Rust command.
        factory.ControlServer.IsRunning = true;
        string snapshotId;
        using (var response = await client.PostAsync("/api/snapshots", CreateSnapshotRequest("fast")))
        {
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            await WaitForSucceededOperationAsync(client, await ReadOperationIdAsync(response));
        }

        using (var response = await client.GetAsync("/api/snapshots"))
        {
            response.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var snapshots = document.RootElement;
            Assert.Single(snapshots.EnumerateArray());
            snapshotId = snapshots[0].GetProperty("id").GetString()
                ?? throw new Xunit.Sdk.XunitException("Snapshot list entry did not contain id.");
        }

        Assert.True(File.Exists(Path.Combine(factory.StoragePath, snapshotId + ".zip")));
        Assert.True(File.Exists(Path.Combine(factory.StoragePath, snapshotId + ".json")));
        using (var archive = ZipFile.OpenRead(Path.Combine(factory.StoragePath, snapshotId + ".zip")))
        {
            Assert.Contains(
                archive.Entries,
                entry => entry.FullName == "oxide/data/MyPlugin/cache/state.log");
        }

        File.WriteAllText(Path.Combine(factory.ServerIdentityPath, "state.txt"), "changed state");

        factory.ControlServer.IsRunning = true;
        using (var response = await client.PostAsync("/api/snapshots/" + snapshotId + "/restore", content: null))
        {
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            await AssertProblemTitleAsync(response, "ServerMustBeStopped");
        }

        factory.ControlServer.IsRunning = false;
        using (var response = await client.PostAsync("/api/snapshots/" + snapshotId + "/restore", content: null))
        {
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            await WaitForSucceededOperationAsync(client, await ReadOperationIdAsync(response));
        }

        Assert.Equal("original state", File.ReadAllText(Path.Combine(factory.ServerIdentityPath, "state.txt")));

        using (var response = await client.DeleteAsync("/api/snapshots/" + snapshotId))
        {
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }

        using (var response = await client.GetAsync("/api/snapshots"))
        {
            response.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Empty(document.RootElement.EnumerateArray());
        }
    }

    [Fact]
    public async Task ConsistentSnapshot_StoppedServer_CreatesArchiveWithoutRcon()
    {
        using var client = factory.CreateClient();
        factory.ControlServer.IsRunning = false;

        using var response = await client.PostAsync(
            "/api/snapshots", CreateSnapshotRequest("consistent"));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        await WaitForSucceededOperationAsync(client, await ReadOperationIdAsync(response));

        using var list = await client.GetAsync("/api/snapshots");
        list.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
        Assert.Contains(document.RootElement.EnumerateArray(), element =>
            element.GetProperty("type").GetString() == "consistent");
    }

    [Fact]
    public async Task SwaggerUi_IsAvailableInDevelopment()
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/swagger/index.html");

        response.EnsureSuccessStatusCode();
        Assert.Contains("Swagger UI", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    private static StringContent CreateSnapshotRequest(string type) =>
        new("{\"type\":\"" + type + "\"}", Encoding.UTF8, "application/json");

    private static async Task<string> ReadOperationIdAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("operationId").GetString()
            ?? throw new InvalidOperationException("Accepted response did not contain operationId.");
    }

    private static async Task<string> WaitForSucceededOperationAsync(HttpClient client, string operationId)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            using var response = await client.GetAsync("/api/operations/" + operationId);
            response.EnsureSuccessStatusCode();

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var status = document.RootElement.GetProperty("status").GetString();
            if (status == "succeeded")
            {
                return operationId;
            }

            if (status == "failed")
            {
                var error = document.RootElement.GetProperty("error").GetString();
                throw new Xunit.Sdk.XunitException("Snapshot operation failed: " + error);
            }

            await Task.Delay(25);
        }

        throw new Xunit.Sdk.XunitException("Snapshot operation did not complete in time.");
    }

    private static async Task AssertProblemTitleAsync(HttpResponseMessage response, string expectedTitle)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(expectedTitle, document.RootElement.GetProperty("title").GetString());
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "RustServer.Snapshots.sln")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Could not locate the RustServer.Snapshots repository root.");
    }

    private static string NormalizeNewlines(string value) => value.Replace("\r\n", "\n", StringComparison.Ordinal);
}
