using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RustServer.Plugins.Features.PluginManagement.Baseline;
using RustServer.Plugins.Features.PluginManagement.Models;
using RustServer.Plugins.Features.PluginManagement.Models.Git;
using Xunit;

namespace RustServer.Plugins.Tests;

public sealed class SourcePreviewApiTests
{
    [Fact]
    public async Task PreviewDoesNotTreatGitIgnoredRuntimeDataAsObsoleteManagedFiles()
    {
        using var plugins = await GitTestRepository.CreateAsync();
        using var configs = await GitTestRepository.CreateAsync();
        using var runtimeRoot = new TemporaryRuntimeRoot();
        await configs.AddRemoteCommitAsync(".gitignore", "data/*\n"u8.ToArray());
        await configs.FetchOriginAsync();
        await configs.ExecuteWorkspaceGitAsync("merge", "--ff-only", "origin/develop");

        var oxideRoot = Path.Combine(runtimeRoot.ServerRootPath, "oxide");
        Directory.CreateDirectory(Path.Combine(oxideRoot, "data"));
        await File.WriteAllTextAsync(
            Path.Combine(oxideRoot, "data", "oxide.users.data"), "keep");

        using var factory = CreateFactory(plugins, configs, runtimeRoot.ServerRootPath);
        using var client = factory.CreateClient();
        using var response = await client.PostAsync(
            "/api/source/preview",
            new StringContent("{\"source\":\"development\"}", Encoding.UTF8, "application/json"));

        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var changes = document.RootElement.GetProperty("changes").EnumerateArray();
        Assert.DoesNotContain(changes, change =>
            change.GetProperty("path").GetString() == "oxide/data/oxide.users.data");
    }

    [Fact]
    public async Task PreviewUsesKnownRemoteHeadsAndLeavesAllObservedStateUnchanged()
    {
        using var plugins = await GitTestRepository.CreateAsync();
        using var configs = await GitTestRepository.CreateAsync();
        using var runtimeRoot = new TemporaryRuntimeRoot();
        var baselinePluginsHead = await plugins.GetHeadAsync();
        var baselineConfigsHead = await configs.GetHeadAsync();
        var targetPluginsHead = await plugins.AddRemoteCommitAsync("Example.cs", "target plugin"u8.ToArray());
        var targetConfigsHead = await configs.AddRemoteCommitAsync("data/Added.bin", [0x00, 0xFF, 0x01]);
        await plugins.FetchOriginAsync();
        await configs.FetchOriginAsync();

        var oxidePath = Path.Combine(runtimeRoot.ServerRootPath, "oxide");
        Directory.CreateDirectory(Path.Combine(oxidePath, "plugins"));
        Directory.CreateDirectory(Path.Combine(oxidePath, "config"));
        await File.WriteAllTextAsync(Path.Combine(oxidePath, "plugins", "Example.cs"), "runtime plugin");
        await File.WriteAllTextAsync(Path.Combine(oxidePath, "config", "Obsolete.json"), "{}");

        using var factory = CreateFactory(plugins, configs, runtimeRoot.ServerRootPath, serverIsRunning: true);
        var baselineStore = factory.Services.GetRequiredService<IBaselineStore>();
        var expectedBaseline = new Baseline(
            PluginEnvironment.Staging,
            Source.Development,
            new GitCommitSha(baselinePluginsHead),
            new GitCommitSha(baselineConfigsHead),
            DateTimeOffset.UtcNow);
        await baselineStore.SaveAsync(expectedBaseline, CancellationToken.None);

        var localPluginsHeadBeforePreview = await plugins.GetHeadAsync();
        var localConfigsHeadBeforePreview = await configs.GetHeadAsync();
        var pluginsBranchBeforePreview = await plugins.GetCurrentBranchAsync();
        var configsBranchBeforePreview = await configs.GetCurrentBranchAsync();
        var runtimePluginBeforePreview = await File.ReadAllBytesAsync(Path.Combine(oxidePath, "plugins", "Example.cs"));
        var obsoleteBeforePreview = await File.ReadAllBytesAsync(Path.Combine(oxidePath, "config", "Obsolete.json"));

        await plugins.AddRemoteCommitAsync("IgnoredUntilFetch.cs", "new remote state"u8.ToArray());
        await configs.AddRemoteCommitAsync("data/IgnoredUntilFetch.bin", [0x02]);

        using var client = factory.CreateClient();
        using var response = await client.PostAsync(
            "/api/source/preview",
            new StringContent("{\"source\":\"development\"}", Encoding.UTF8, "application/json"));

        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        Assert.Equal("staging", root.GetProperty("environment").GetString());
        Assert.Equal("development", root.GetProperty("currentSource").GetProperty("type").GetString());
        Assert.Equal(targetPluginsHead, root.GetProperty("targetGit").GetProperty("pluginsHead").GetString());
        Assert.Equal(targetConfigsHead, root.GetProperty("targetGit").GetProperty("configsHead").GetString());
        Assert.Equal(1, root.GetProperty("counts").GetProperty("added").GetInt32());
        Assert.Equal(1, root.GetProperty("counts").GetProperty("modified").GetInt32());
        Assert.Equal(1, root.GetProperty("counts").GetProperty("deleted").GetInt32());

        var changes = root.GetProperty("changes").EnumerateArray().ToArray();
        AssertChange(changes, "configs", "oxide/config/Obsolete.json", "deleted");
        AssertChange(changes, "configs", "oxide/data/Added.bin", "added");
        AssertChange(changes, "plugins", "oxide/plugins/Example.cs", "modified");
        Assert.Equal(localPluginsHeadBeforePreview, await plugins.GetHeadAsync());
        Assert.Equal(localConfigsHeadBeforePreview, await configs.GetHeadAsync());
        Assert.Equal(pluginsBranchBeforePreview, await plugins.GetCurrentBranchAsync());
        Assert.Equal(configsBranchBeforePreview, await configs.GetCurrentBranchAsync());
        Assert.Equal(runtimePluginBeforePreview, await File.ReadAllBytesAsync(Path.Combine(oxidePath, "plugins", "Example.cs")));
        Assert.Equal(obsoleteBeforePreview, await File.ReadAllBytesAsync(Path.Combine(oxidePath, "config", "Obsolete.json")));
        Assert.Equal(expectedBaseline, await baselineStore.GetAsync(CancellationToken.None));
    }

    [Fact]
    public async Task PreviewRejectsIncompleteReleaseWithTheMissingRepository()
    {
        using var plugins = await GitTestRepository.CreateAsync();
        using var configs = await GitTestRepository.CreateAsync();
        using var runtimeRoot = new TemporaryRuntimeRoot();
        await plugins.AddRemoteReleaseAsync("2.0");
        await plugins.FetchOriginAsync();
        await configs.FetchOriginAsync();
        using var factory = CreateFactory(plugins, configs, runtimeRoot.ServerRootPath);
        using var client = factory.CreateClient();

        using var response = await client.PostAsync(
            "/api/source/preview",
            new StringContent("{\"source\":\"release\",\"release\":\"2.0\"}", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("ReleaseIncomplete", document.RootElement.GetProperty("code").GetString());
        var missingRepositories = document.RootElement
            .GetProperty("details")
            .GetProperty("missingRepositories")
            .EnumerateArray()
            .Select(item => item.GetString())
            .ToArray();
        Assert.Equal("configs", Assert.Single(missingRepositories));
    }

    [Fact]
    public async Task PreviewTreatsReleaseAsALiteralRefAndRejectsMalformedRelease()
    {
        using var plugins = await GitTestRepository.CreateAsync();
        using var configs = await GitTestRepository.CreateAsync();
        using var runtimeRoot = new TemporaryRuntimeRoot();
        await plugins.AddRemoteCommitAsync("release-candidate.txt");
        await configs.AddRemoteCommitAsync("release-candidate.json");
        await plugins.AddRemoteReleaseAsync("2.0");
        await configs.AddRemoteReleaseAsync("2.0");
        await plugins.FetchOriginAsync();
        await configs.FetchOriginAsync();
        using var factory = CreateFactory(plugins, configs, runtimeRoot.ServerRootPath);
        using var client = factory.CreateClient();

        using var revisionLikeResponse = await client.PostAsync(
            "/api/source/preview",
            new StringContent("{\"source\":\"release\",\"release\":\"2.0^\"}", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.Conflict, revisionLikeResponse.StatusCode);
        using var revisionLikeError = JsonDocument.Parse(await revisionLikeResponse.Content.ReadAsStringAsync());
        Assert.Equal("TargetSourceUnavailable", revisionLikeError.RootElement.GetProperty("code").GetString());

        using var malformedResponse = await client.PostAsync(
            "/api/source/preview",
            new StringContent("{\"source\":\"release\",\"release\":\"release/2.0\"}", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, malformedResponse.StatusCode);
        using var malformedError = JsonDocument.Parse(await malformedResponse.Content.ReadAsStringAsync());
        Assert.Equal("InvalidSource", malformedError.RootElement.GetProperty("code").GetString());
    }

    private static PluginApplicationFactory CreateFactory(
        GitTestRepository plugins,
        GitTestRepository configs,
        string runtimeServerRootPath,
        bool serverIsRunning = false) =>
        new(new Dictionary<string, string?>
        {
            ["PluginManagement:StateRootPath"] = Path.Combine(plugins.RootPath, "state"),
            ["PluginManagement:Repositories:Plugins:WorkspacePath"] = plugins.WorkspacePath,
            ["PluginManagement:Repositories:Configs:WorkspacePath"] = configs.WorkspacePath,
            ["PluginManagement:Runtime:ServerRootPath"] = runtimeServerRootPath
        }, serverIsRunning);

    private static void AssertChange(
        IEnumerable<JsonElement> changes,
        string repository,
        string path,
        string change) =>
        Assert.Contains(
            changes,
            item => item.GetProperty("repository").GetString() == repository &&
                    item.GetProperty("path").GetString() == path &&
                    item.GetProperty("change").GetString() == change);

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
