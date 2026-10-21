using System.Net;
using System.Text.Json;
using Xunit;

namespace RustServer.Plugins.Tests;

public sealed class GitApiTests
{
    [Fact]
    public async Task StatusDoesNotFetchAndExplicitFetchUpdatesOnlyRemoteTrackingReferences()
    {
        using var plugins = await GitTestRepository.CreateAsync();
        using var configs = await GitTestRepository.CreateAsync();
        using var factory = CreateFactory(plugins, configs);
        using var client = factory.CreateClient();
        var previousPluginsHead = await plugins.GetHeadAsync();
        var previousConfigsHead = await configs.GetHeadAsync();
        var updatedPluginsHead = await plugins.AddRemoteCommitAsync("plugins-update.txt");
        var updatedConfigsHead = await configs.AddRemoteCommitAsync("configs-update.txt");

        using (var statusBeforeFetch = await client.GetAsync("/api/git/status"))
        {
            statusBeforeFetch.EnsureSuccessStatusCode();

            using var document = JsonDocument.Parse(await statusBeforeFetch.Content.ReadAsStringAsync());
            Assert.Equal("ready", document.RootElement.GetProperty("gitStatus").GetString());
            Assert.Null(document.RootElement.GetProperty("lastFetchAt").GetString());
            Assert.Equal(
                previousPluginsHead,
                document.RootElement.GetProperty("plugins").GetProperty("remoteHead").GetString());
            Assert.Equal(
                previousConfigsHead,
                document.RootElement.GetProperty("configs").GetProperty("remoteHead").GetString());
        }

        using (var fetchResponse = await client.PostAsync("/api/git/fetch", content: null))
        {
            Assert.Equal(HttpStatusCode.Accepted, fetchResponse.StatusCode);

            using var document = JsonDocument.Parse(await fetchResponse.Content.ReadAsStringAsync());
            Assert.True(document.RootElement.TryGetProperty("operationId", out _));
        }

        Assert.Equal(previousPluginsHead, await plugins.GetHeadAsync());
        Assert.Equal(previousConfigsHead, await configs.GetHeadAsync());

        using var statusAfterFetch = await client.GetAsync("/api/git/status");
        statusAfterFetch.EnsureSuccessStatusCode();

        using var statusDocument = JsonDocument.Parse(await statusAfterFetch.Content.ReadAsStringAsync());
        Assert.Equal("degraded", statusDocument.RootElement.GetProperty("gitStatus").GetString());
        Assert.NotNull(statusDocument.RootElement.GetProperty("lastFetchAt").GetString());
        Assert.Equal(
            previousPluginsHead,
            statusDocument.RootElement.GetProperty("plugins").GetProperty("localHead").GetString());
        Assert.Equal(
            updatedConfigsHead,
            statusDocument.RootElement.GetProperty("configs").GetProperty("remoteHead").GetString());
        Assert.Equal("behind", statusDocument.RootElement.GetProperty("plugins").GetProperty("relation").GetString());
    }

    [Fact]
    public async Task FetchLeavesDirtyDevelopmentUnchanged()
    {
        using var plugins = await GitTestRepository.CreateAsync();
        using var configs = await GitTestRepository.CreateAsync();
        using var factory = CreateFactory(plugins, configs);
        using var client = factory.CreateClient();
        var previousPluginsHead = await plugins.GetHeadAsync();
        var previousConfigsHead = await configs.GetHeadAsync();
        await File.WriteAllTextAsync(Path.Combine(plugins.WorkspacePath, "pending.txt"), "pending");
        var updatedPluginsHead = await plugins.AddRemoteCommitAsync("plugins-update.txt");
        var updatedConfigsHead = await configs.AddRemoteCommitAsync("configs-update.txt");

        using var fetchResponse = await client.PostAsync("/api/git/fetch", content: null);
        Assert.Equal(HttpStatusCode.Accepted, fetchResponse.StatusCode);

        Assert.Equal(previousPluginsHead, await plugins.GetHeadAsync());
        Assert.Equal(updatedPluginsHead, await plugins.GetTrackingHeadAsync());
        Assert.Equal(previousConfigsHead, await configs.GetHeadAsync());

        using var statusResponse = await client.GetAsync("/api/git/status");
        statusResponse.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(await statusResponse.Content.ReadAsStringAsync());
        var pluginStatus = document.RootElement.GetProperty("plugins");
        Assert.Equal("dirty", pluginStatus.GetProperty("workspace").GetString());
        Assert.Equal("behind", pluginStatus.GetProperty("relation").GetString());
        Assert.Equal(previousPluginsHead, pluginStatus.GetProperty("localHead").GetString());
        Assert.Equal(updatedPluginsHead, pluginStatus.GetProperty("remoteHead").GetString());
    }

    [Fact]
    public async Task FetchPreservesDivergedBranches()
    {
        using var plugins = await GitTestRepository.CreateAsync();
        using var configs = await GitTestRepository.CreateAsync();
        using var factory = CreateFactory(plugins, configs);
        using var client = factory.CreateClient();
        var localHead = await plugins.AddWorkspaceCommitAsync("local.txt", "local"u8.ToArray());
        var remoteHead = await plugins.AddRemoteCommitAsync("remote.txt");

        using var response = await client.PostAsync("/api/git/fetch", content: null);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(localHead, await plugins.GetHeadAsync());
        Assert.Equal(remoteHead, await plugins.GetTrackingHeadAsync());
        using var statusResponse = await client.GetAsync("/api/git/status");
        statusResponse.EnsureSuccessStatusCode();
        using var status = JsonDocument.Parse(await statusResponse.Content.ReadAsStringAsync());
        Assert.Equal("diverged", status.RootElement.GetProperty("plugins").GetProperty("relation").GetString());
    }

    [Fact]
    public async Task FetchUpdatesRemoteTrackingWhileRustIsRunning()
    {
        using var plugins = await GitTestRepository.CreateAsync();
        using var configs = await GitTestRepository.CreateAsync();
        using var factory = CreateFactory(plugins, configs, serverIsRunning: true);
        using var client = factory.CreateClient();
        var previousPluginsHead = await plugins.GetHeadAsync();
        var previousConfigsHead = await configs.GetHeadAsync();
        await plugins.AddRemoteCommitAsync("plugins-update.txt");
        await configs.AddRemoteCommitAsync("configs-update.txt");

        using var response = await client.PostAsync("/api/git/fetch", content: null);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(previousPluginsHead, await plugins.GetHeadAsync());
        Assert.Equal(previousConfigsHead, await configs.GetHeadAsync());
        Assert.NotEqual(previousPluginsHead, await plugins.GetTrackingHeadAsync());
        Assert.NotEqual(previousConfigsHead, await configs.GetTrackingHeadAsync());
    }

    private static PluginApplicationFactory CreateFactory(
        GitTestRepository plugins,
        GitTestRepository configs,
        bool serverIsRunning = false) =>
        new(new Dictionary<string, string?>
        {
            ["PluginManagement:StateRootPath"] = Path.Combine(plugins.RootPath, "state"),
            ["PluginManagement:Repositories:Plugins:WorkspacePath"] = plugins.WorkspacePath,
            ["PluginManagement:Repositories:Configs:WorkspacePath"] = configs.WorkspacePath
        }, serverIsRunning);
}
