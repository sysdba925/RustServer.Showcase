using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RustServer.Plugins.Features.PluginManagement.Baseline;
using RustServer.Plugins.Features.PluginManagement.Models;
using RustServer.Plugins.Features.PluginManagement.Models.Git;
using Xunit;

namespace RustServer.Plugins.Tests;

public sealed class RuntimeDiffContentApiTests
{
    [Fact]
    public async Task ReadsBaselineContentFromSavedCommitInsteadOfCurrentWorkspaceHead()
    {
        using var plugins = await GitTestRepository.CreateAsync();
        using var configs = await GitTestRepository.CreateAsync();
        using var runtimeRoot = new TemporaryRuntimeRoot();
        var baselineCommit = await configs.AddWorkspaceCommitAsync("config/Test.json", "{\"value\":\"baseline\"}"u8.ToArray());
        await configs.AddWorkspaceCommitAsync("config/Test.json", "{\"value\":\"current-head\"}"u8.ToArray());
        var oxideRoot = Path.Combine(runtimeRoot.ServerRootPath, "oxide");
        Directory.CreateDirectory(Path.Combine(oxideRoot, "config"));
        await File.WriteAllTextAsync(Path.Combine(oxideRoot, "config", "Test.json"), "{\"value\":\"runtime\"}");

        using var factory = CreateFactory(plugins, configs, runtimeRoot.ServerRootPath);
        var baselineStore = factory.Services.GetRequiredService<IBaselineStore>();
        await baselineStore.SaveAsync(
            new Baseline(
                PluginEnvironment.Staging,
                Source.Development,
                new GitCommitSha(await plugins.GetHeadAsync()),
                new GitCommitSha(baselineCommit),
                DateTimeOffset.UtcNow),
            CancellationToken.None);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/runtime/diff/content?repository=configs&path=oxide%2Fconfig%2FTest.json");
        response.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var content = document.RootElement.GetProperty("content");
        Assert.Equal("ready", document.RootElement.GetProperty("status").GetString());
        Assert.Equal("modified", content.GetProperty("change").GetString());
        Assert.Equal("text", content.GetProperty("baseline").GetProperty("kind").GetString());
        Assert.Equal("{\"value\":\"baseline\"}", content.GetProperty("baseline").GetProperty("text").GetString());
        Assert.Equal("{\"value\":\"runtime\"}", content.GetProperty("runtime").GetProperty("text").GetString());
    }

    [Fact]
    public async Task ReturnsMissingAndBinarySidesForAddedAndDeletedFiles()
    {
        using var plugins = await GitTestRepository.CreateAsync();
        using var configs = await GitTestRepository.CreateAsync();
        using var runtimeRoot = new TemporaryRuntimeRoot();
        var baselineCommit = await configs.AddWorkspaceCommitAsync("data/Deleted.bin", [0x00, 0xFF]);
        var oxideRoot = Path.Combine(runtimeRoot.ServerRootPath, "oxide");
        Directory.CreateDirectory(Path.Combine(oxideRoot, "data"));
        await File.WriteAllBytesAsync(Path.Combine(oxideRoot, "data", "Added.bin"), [0x00, 0x01]);

        using var factory = CreateFactory(plugins, configs, runtimeRoot.ServerRootPath);
        var baselineStore = factory.Services.GetRequiredService<IBaselineStore>();
        await baselineStore.SaveAsync(
            new Baseline(
                PluginEnvironment.Staging,
                Source.Development,
                new GitCommitSha(await plugins.GetHeadAsync()),
                new GitCommitSha(baselineCommit),
                DateTimeOffset.UtcNow),
            CancellationToken.None);
        using var client = factory.CreateClient();

        await AssertContentAsync(
            client,
            "oxide/data/Added.bin",
            "added",
            "missing",
            "binary");
        await AssertContentAsync(
            client,
            "oxide/data/Deleted.bin",
            "deleted",
            "binary",
            "missing");
    }

    [Fact]
    public async Task RejectsUnmanagedAndUnchangedEntries()
    {
        using var plugins = await GitTestRepository.CreateAsync();
        using var configs = await GitTestRepository.CreateAsync();
        using var runtimeRoot = new TemporaryRuntimeRoot();
        var baselineCommit = await configs.AddWorkspaceCommitAsync("config/Unchanged.json", "{}"u8.ToArray());
        var oxideRoot = Path.Combine(runtimeRoot.ServerRootPath, "oxide");
        Directory.CreateDirectory(Path.Combine(oxideRoot, "config"));
        await File.WriteAllTextAsync(Path.Combine(oxideRoot, "config", "Unchanged.json"), "{}");

        using var factory = CreateFactory(plugins, configs, runtimeRoot.ServerRootPath);
        var baselineStore = factory.Services.GetRequiredService<IBaselineStore>();
        await baselineStore.SaveAsync(
            new Baseline(
                PluginEnvironment.Staging,
                Source.Development,
                new GitCommitSha(await plugins.GetHeadAsync()),
                new GitCommitSha(baselineCommit),
                DateTimeOffset.UtcNow),
            CancellationToken.None);
        using var client = factory.CreateClient();

        using var invalidResponse = await client.GetAsync("/api/runtime/diff/content?repository=configs&path=oxide%2Fplugins%2FNotAllowed.cs");
        Assert.Equal(HttpStatusCode.BadRequest, invalidResponse.StatusCode);
        await AssertErrorCodeAsync(invalidResponse, "InvalidRuntimeDiffEntry");

        using var unchangedResponse = await client.GetAsync("/api/runtime/diff/content?repository=configs&path=oxide%2Fconfig%2FUnchanged.json");
        Assert.Equal(HttpStatusCode.NotFound, unchangedResponse.StatusCode);
        await AssertErrorCodeAsync(unchangedResponse, "RuntimeDiffEntryNotFound");
    }

    [Fact]
    public async Task MissingBaselineReturnsNotInitializedWithoutAccessingContentDependencies()
    {
        using var stateRoot = new TemporaryStateRoot();
        using var factory = new PluginApplicationFactory(new Dictionary<string, string?>
        {
            ["PluginManagement:StateRootPath"] = stateRoot.Path
        });
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/runtime/diff/content?repository=configs&path=oxide%2Fconfig%2FTest.json");
        response.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("notInitialized", document.RootElement.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("content").ValueKind);
    }

    private static async Task AssertContentAsync(
        HttpClient client,
        string path,
        string expectedChange,
        string expectedBaselineKind,
        string expectedRuntimeKind)
    {
        using var response = await client.GetAsync($"/api/runtime/diff/content?repository=configs&path={Uri.EscapeDataString(path)}");
        response.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var content = document.RootElement.GetProperty("content");
        Assert.Equal(expectedChange, content.GetProperty("change").GetString());
        Assert.Equal(expectedBaselineKind, content.GetProperty("baseline").GetProperty("kind").GetString());
        Assert.Equal(expectedRuntimeKind, content.GetProperty("runtime").GetProperty("kind").GetString());
        Assert.Equal(JsonValueKind.Null, content.GetProperty("baseline").GetProperty("text").ValueKind);
        Assert.Equal(JsonValueKind.Null, content.GetProperty("runtime").GetProperty("text").ValueKind);
    }

    private static async Task AssertErrorCodeAsync(HttpResponseMessage response, string expectedCode)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(expectedCode, document.RootElement.GetProperty("code").GetString());
    }

    private static PluginApplicationFactory CreateFactory(
        GitTestRepository plugins,
        GitTestRepository configs,
        string runtimeServerRootPath) =>
        new(new Dictionary<string, string?>
        {
            ["PluginManagement:StateRootPath"] = Path.Combine(plugins.RootPath, "state"),
            ["PluginManagement:Repositories:Plugins:WorkspacePath"] = plugins.WorkspacePath,
            ["PluginManagement:Repositories:Configs:WorkspacePath"] = configs.WorkspacePath,
            ["PluginManagement:Runtime:ServerRootPath"] = runtimeServerRootPath
        });

    private sealed class TemporaryRuntimeRoot : IDisposable
    {
        public string RootPath { get; } = Path.Combine(
            Path.GetTempPath(),
            "RustServer.Plugins.Tests",
            Guid.NewGuid().ToString("N"));

        public string ServerRootPath => Path.Combine(RootPath, "server");

        public void Dispose()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }
    }

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
