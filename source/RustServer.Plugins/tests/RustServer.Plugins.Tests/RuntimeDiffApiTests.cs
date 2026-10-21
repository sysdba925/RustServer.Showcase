using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RustServer.Plugins.Features.PluginManagement.Baseline;
using RustServer.Plugins.Features.PluginManagement.Models;
using RustServer.Plugins.Features.PluginManagement.Models.Git;
using Xunit;

namespace RustServer.Plugins.Tests;

public sealed class RuntimeDiffApiTests
{
    [Fact]
    public async Task RuntimeDiff_ExcludesGitIgnoredFilesAndKeepsTrackedOrExplicitlyUnignoredFiles()
    {
        using var plugins = await GitTestRepository.CreateAsync();
        using var configs = await GitTestRepository.CreateAsync();
        using var runtimeRoot = new TemporaryRuntimeRoot();
        await configs.AddWorkspaceCommitAsync("data/Tracked.json", "baseline"u8.ToArray());
        var baselineCommit = await configs.AddWorkspaceCommitAsync(
            ".gitignore",
            "data/*\n!data/Kits/\ndata/Kits/*\n!data/Kits/kits_data.json\n"u8.ToArray());

        var oxideRoot = Path.Combine(runtimeRoot.ServerRootPath, "oxide");
        Directory.CreateDirectory(Path.Combine(oxideRoot, "data", "Kits"));
        Directory.CreateDirectory(Path.Combine(oxideRoot, "config"));
        await File.WriteAllTextAsync(Path.Combine(oxideRoot, "data", "Tracked.json"), "updated");
        await File.WriteAllTextAsync(Path.Combine(oxideRoot, "data", "ActivityRewards.json"), "ignore");
        await File.WriteAllTextAsync(Path.Combine(oxideRoot, "data", "Kits", "player_data.json"), "ignore");
        await File.WriteAllTextAsync(Path.Combine(oxideRoot, "data", "Kits", "kits_data.json"), "allow");
        await File.WriteAllTextAsync(Path.Combine(oxideRoot, "config", "Allowed.json"), "allow");

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
        using var response = await client.GetAsync("/api/runtime/diff");
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        var changes = document.RootElement.GetProperty("changes").EnumerateArray().ToArray();
        Assert.Equal(3, changes.Length);
        AssertChange(changes[0], "configs", "oxide/config/Allowed.json", "added");
        AssertChange(changes[1], "configs", "oxide/data/Kits/kits_data.json", "added");
        AssertChange(changes[2], "configs", "oxide/data/Tracked.json", "modified");
        Assert.DoesNotContain(changes, item =>
            item.GetProperty("path").GetString()!.Contains("player_data", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ClassifiesAddedModifiedAndDeletedFilesAgainstBaselineCommits()
    {
        using var plugins = await GitTestRepository.CreateAsync();
        using var configs = await GitTestRepository.CreateAsync();
        using var runtimeRoot = new TemporaryRuntimeRoot();
        var baselineConfigsCommit = await configs.AddWorkspaceCommitAsync(
            "data/Modified.bin",
            [0x00, 0xFF, 0x01]);
        baselineConfigsCommit = await configs.AddWorkspaceCommitAsync(
            "config/Deleted.json",
            [0x7B, 0x7D]);
        var runtimeBinary = new byte[] { 0x00, 0xFF, 0x02 };
        var currentConfigsHead = await configs.AddWorkspaceCommitAsync("data/Modified.bin", runtimeBinary);
        var oxideRoot = Path.Combine(runtimeRoot.ServerRootPath, "oxide");
        Directory.CreateDirectory(Path.Combine(oxideRoot, "config"));
        Directory.CreateDirectory(Path.Combine(oxideRoot, "data"));
        await File.WriteAllBytesAsync(Path.Combine(oxideRoot, "data", "Modified.bin"), runtimeBinary);
        await File.WriteAllTextAsync(Path.Combine(oxideRoot, "config", "Added.json"), "{}");

        using var factory = CreateFactory(plugins, configs, runtimeRoot.ServerRootPath);
        var baselineStore = factory.Services.GetRequiredService<IBaselineStore>();
        await baselineStore.SaveAsync(
            new Baseline(
                PluginEnvironment.Staging,
                Source.Development,
                new GitCommitSha(await plugins.GetHeadAsync()),
                new GitCommitSha(baselineConfigsCommit),
                DateTimeOffset.UtcNow),
            CancellationToken.None);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/runtime/diff");
        response.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("ready", document.RootElement.GetProperty("status").GetString());
        var changes = document.RootElement.GetProperty("changes").EnumerateArray().ToArray();
        Assert.Equal(3, changes.Length);
        AssertChange(changes[0], "configs", "oxide/config/Added.json", "added");
        AssertChange(changes[1], "configs", "oxide/config/Deleted.json", "deleted");
        AssertChange(changes[2], "configs", "oxide/data/Modified.bin", "modified");
        Assert.Equal(currentConfigsHead, await configs.GetHeadAsync());
    }

    [Fact]
    public async Task MissingBaseline_ReturnsNotInitializedWithoutReadingGitOrRuntime()
    {
        using var stateRoot = new TemporaryStateRoot();
        using var factory = new PluginApplicationFactory(new Dictionary<string, string?>
        {
            ["PluginManagement:StateRootPath"] = stateRoot.Path
        });
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/runtime/diff");
        response.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("notInitialized", document.RootElement.GetProperty("status").GetString());
        Assert.Empty(document.RootElement.GetProperty("changes").EnumerateArray());
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

    private static void AssertChange(JsonElement change, string repository, string path, string kind)
    {
        Assert.Equal(repository, change.GetProperty("repository").GetString());
        Assert.Equal(path, change.GetProperty("path").GetString());
        Assert.Equal(kind, change.GetProperty("change").GetString());
    }

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
