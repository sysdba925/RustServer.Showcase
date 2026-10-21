using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RustServer.Plugins.Features.PluginManagement.Baseline;
using RustServer.Plugins.Features.PluginManagement.Models;
using RustServer.Plugins.Features.PluginManagement.Models.Git;
using Xunit;

namespace RustServer.Plugins.Tests;

public sealed class BaselineApiTests
{
    [Fact]
    public async Task GetBaseline_DistinguishesMissingAndCorruptedState()
    {
        using var stateRoot = new TemporaryStateRoot();
        using var factory = CreateFactory(stateRoot.Path);
        using var client = factory.CreateClient();

        using (var missingResponse = await client.GetAsync("/api/baseline"))
        {
            missingResponse.EnsureSuccessStatusCode();
            Assert.False(File.Exists(Path.Combine(stateRoot.Path, "baseline.json")));

            using var document = JsonDocument.Parse(await missingResponse.Content.ReadAsStringAsync());
            Assert.Equal("notInitialized", document.RootElement.GetProperty("status").GetString());
            Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("baseline").ValueKind);
        }

        Directory.CreateDirectory(stateRoot.Path);
        await File.WriteAllTextAsync(Path.Combine(stateRoot.Path, "baseline.json"), "{");

        using (var corruptedResponse = await client.GetAsync("/api/baseline"))
        {
            Assert.Equal(HttpStatusCode.InternalServerError, corruptedResponse.StatusCode);

            using var document = JsonDocument.Parse(await corruptedResponse.Content.ReadAsStringAsync());
            Assert.Equal("BaselineStateCorrupted", document.RootElement.GetProperty("code").GetString());
        }

        using var environmentResponse = await client.GetAsync("/api/environment");
        environmentResponse.EnsureSuccessStatusCode();

        using var environmentDocument = JsonDocument.Parse(await environmentResponse.Content.ReadAsStringAsync());
        Assert.Equal("degraded", environmentDocument.RootElement.GetProperty("systemState").GetString());
    }

    [Fact]
    public async Task SavedBaseline_PersistsAcrossApplicationRestart()
    {
        using var stateRoot = new TemporaryStateRoot();
        var expectedAppliedAt = new DateTimeOffset(2026, 10, 8, 13, 0, 0, TimeSpan.Zero);

        using (var initialFactory = CreateFactory(stateRoot.Path))
        {
            var store = initialFactory.Services.GetRequiredService<IBaselineStore>();
            await store.SaveAsync(
                new Baseline(
                    PluginEnvironment.Production,
                    Source.ForRelease(new Release("2.0")),
                    new GitCommitSha("a1b2c3d"),
                    new GitCommitSha("d4e5f6a"),
                    expectedAppliedAt),
                CancellationToken.None);
        }

        using var restartedFactory = CreateFactory(stateRoot.Path);
        using var client = restartedFactory.CreateClient();
        using var baselineResponse = await client.GetAsync("/api/baseline");
        baselineResponse.EnsureSuccessStatusCode();

        using (var document = JsonDocument.Parse(await baselineResponse.Content.ReadAsStringAsync()))
        {
            Assert.Equal("ready", document.RootElement.GetProperty("status").GetString());
            var baseline = document.RootElement.GetProperty("baseline");
            Assert.Equal("production", baseline.GetProperty("environment").GetString());
            Assert.Equal("release", baseline.GetProperty("source").GetString());
            Assert.Equal("2.0", baseline.GetProperty("release").GetString());
            Assert.Equal("a1b2c3d", baseline.GetProperty("pluginsCommit").GetString());
            Assert.Equal("d4e5f6a", baseline.GetProperty("configsCommit").GetString());
            Assert.Equal(expectedAppliedAt, baseline.GetProperty("appliedAt").GetDateTimeOffset());
        }

        using var environmentResponse = await client.GetAsync("/api/environment");
        environmentResponse.EnsureSuccessStatusCode();

        using var environmentDocument = JsonDocument.Parse(await environmentResponse.Content.ReadAsStringAsync());
        Assert.Equal("degraded", environmentDocument.RootElement.GetProperty("systemState").GetString());
    }

    private static PluginApplicationFactory CreateFactory(string stateRootPath) =>
        new(new Dictionary<string, string?>
        {
            ["PluginManagement:StateRootPath"] = stateRootPath
        });

    private sealed class TemporaryStateRoot : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "RustServer.Plugins.Tests",
            Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
