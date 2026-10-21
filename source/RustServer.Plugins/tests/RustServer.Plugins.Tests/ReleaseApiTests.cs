using System.Net;
using System.Text;
using System.Text.Json;
using Xunit;

namespace RustServer.Plugins.Tests;

public sealed class ReleaseApiTests
{
    [Fact]
    public async Task AppliesReleasesForwardAndBackwardThroughTheSameApplyFlowAndMarksTheBaselineReleaseCurrent()
    {
        using var plugins = await GitTestRepository.CreateAsync();
        using var configs = await GitTestRepository.CreateAsync();
        using var runtimeRoot = new TemporaryRuntimeRoot();
        var pluginsReleaseOneHead = await plugins.AddRemoteReleaseAsync("1.0");
        var configsReleaseOneHead = await configs.AddRemoteReleaseAsync("1.0");
        var pluginsReleaseTwoHead = await plugins.AddRemoteCommitAsync("Example.cs", "release two"u8.ToArray());
        var configsReleaseTwoHead = await configs.AddRemoteCommitAsync("config/Example.json", "{}"u8.ToArray());
        await plugins.AddRemoteReleaseAsync("2.0");
        await configs.AddRemoteReleaseAsync("2.0");
        Directory.CreateDirectory(Path.Combine(runtimeRoot.ServerRootPath, "oxide"));

        using var factory = CreateFactory(plugins, configs, runtimeRoot.ServerRootPath);
        using var client = factory.CreateClient();

        await ApplyReleaseAsync(client, "2.0");

        var releasesAfterForwardApply = await GetReleasesAsync(client);
        AssertCurrentRelease(
            releasesAfterForwardApply,
            "2.0",
            pluginsReleaseTwoHead,
            configsReleaseTwoHead);

        await plugins.AddRemoteCommitToBranchAsync("release/2.0", "Example.cs", "remote hotfix"u8.ToArray());
        await configs.AddRemoteCommitToBranchAsync("release/2.0", "config/Example.json", "{\"hotfix\":true}"u8.ToArray());
        await plugins.FetchOriginAsync();
        await configs.FetchOriginAsync();

        var releasesAfterRemoteHotfix = await GetReleasesAsync(client);
        var releaseWithChangedGit = Assert.Single(
            releasesAfterRemoteHotfix,
            release => release.GetProperty("version").GetString() == "2.0");
        Assert.True(releaseWithChangedGit.GetProperty("current").GetBoolean());
        Assert.False(releaseWithChangedGit.GetProperty("matchesBaseline").GetBoolean());

        await ApplyReleaseAsync(client, "1.0");

        var releasesAfterRollbackApply = await GetReleasesAsync(client);
        AssertCurrentRelease(
            releasesAfterRollbackApply,
            "1.0",
            pluginsReleaseOneHead,
            configsReleaseOneHead);
    }

    [Fact]
    public async Task ListsCompleteReleaseKnownInBothRemoteTrackingRepositories()
    {
        using var plugins = await GitTestRepository.CreateAsync();
        using var configs = await GitTestRepository.CreateAsync();
        var pluginsReleaseHead = await plugins.AddRemoteReleaseAsync("2.0");
        var configsReleaseHead = await configs.AddRemoteReleaseAsync("2.0");
        using var factory = CreateFactory(plugins, configs);
        using var client = factory.CreateClient();

        using (var fetchResponse = await client.PostAsync("/api/git/fetch", content: null))
        {
            Assert.Equal(HttpStatusCode.Accepted, fetchResponse.StatusCode);
        }

        using var response = await client.GetAsync("/api/releases");
        response.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var release = Assert.Single(document.RootElement.GetProperty("releases").EnumerateArray());
        Assert.Equal("2.0", release.GetProperty("version").GetString());
        Assert.Equal("release/2.0", release.GetProperty("branch").GetString());
        Assert.Equal("complete", release.GetProperty("status").GetString());
        Assert.Equal(pluginsReleaseHead, release.GetProperty("pluginsHead").GetString());
        Assert.Equal(configsReleaseHead, release.GetProperty("configsHead").GetString());
    }

    [Fact]
    public async Task ListsIncompleteReleaseWithoutChangingGitWorkspace()
    {
        using var plugins = await GitTestRepository.CreateAsync();
        using var configs = await GitTestRepository.CreateAsync();
        var pluginsReleaseHead = await plugins.AddRemoteReleaseAsync("3.0");
        using var factory = CreateFactory(plugins, configs);
        using var client = factory.CreateClient();

        using (var fetchResponse = await client.PostAsync("/api/git/fetch", content: null))
        {
            Assert.Equal(HttpStatusCode.Accepted, fetchResponse.StatusCode);
        }

        var pluginsHeadBeforeRead = await plugins.GetHeadAsync();
        var pluginsBranchBeforeRead = await plugins.GetCurrentBranchAsync();
        var configsHeadBeforeRead = await configs.GetHeadAsync();
        var configsBranchBeforeRead = await configs.GetCurrentBranchAsync();

        using var response = await client.GetAsync("/api/releases");
        response.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var release = Assert.Single(document.RootElement.GetProperty("releases").EnumerateArray());
        Assert.Equal("3.0", release.GetProperty("version").GetString());
        Assert.Equal("incomplete", release.GetProperty("status").GetString());
        Assert.Equal(pluginsReleaseHead, release.GetProperty("pluginsHead").GetString());
        Assert.Equal(JsonValueKind.Null, release.GetProperty("configsHead").ValueKind);
        Assert.Equal(pluginsHeadBeforeRead, await plugins.GetHeadAsync());
        Assert.Equal(pluginsBranchBeforeRead, await plugins.GetCurrentBranchAsync());
        Assert.Equal(configsHeadBeforeRead, await configs.GetHeadAsync());
        Assert.Equal(configsBranchBeforeRead, await configs.GetCurrentBranchAsync());
    }

    private static PluginApplicationFactory CreateFactory(
        GitTestRepository plugins,
        GitTestRepository configs,
        string? runtimeServerRootPath = null) =>
        new(new Dictionary<string, string?>
        {
            ["PluginManagement:StateRootPath"] = Path.Combine(plugins.RootPath, "state"),
            ["PluginManagement:Repositories:Plugins:WorkspacePath"] = plugins.WorkspacePath,
            ["PluginManagement:Repositories:Configs:WorkspacePath"] = configs.WorkspacePath,
            ["PluginManagement:Runtime:ServerRootPath"] = runtimeServerRootPath ??
                Path.Combine(plugins.RootPath, "server")
        });

    private static async Task ApplyReleaseAsync(HttpClient client, string version)
    {
        using var response = await client.PostAsync(
            "/api/source/apply",
            new StringContent(
                $$"""{"source":"release","release":"{{version}}"}""",
                Encoding.UTF8,
                "application/json"));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    private static async Task<JsonElement[]> GetReleasesAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/api/releases");
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("releases").EnumerateArray()
            .Select(release => release.Clone())
            .ToArray();
    }

    private static void AssertCurrentRelease(
        IEnumerable<JsonElement> releases,
        string version,
        string pluginsHead,
        string configsHead)
    {
        var current = Assert.Single(releases, release => release.GetProperty("current").GetBoolean());
        Assert.Equal(version, current.GetProperty("version").GetString());
        Assert.True(current.GetProperty("matchesBaseline").GetBoolean());
        Assert.Equal(pluginsHead, current.GetProperty("pluginsHead").GetString());
        Assert.Equal(configsHead, current.GetProperty("configsHead").GetString());
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
}
