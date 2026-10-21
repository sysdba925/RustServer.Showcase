using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using RustServer.Plugins.Features.PluginManagement.Git;
using RustServer.Plugins.Features.PluginManagement.LocalGit;
using RustServer.Plugins.Features.PluginManagement.Models;
using RustServer.Plugins.Features.PluginManagement.Models.Git;
using RustServer.Plugins.Features.PluginManagement.RemoteGit;
using Xunit;

namespace RustServer.Plugins.Tests;

public sealed class GitSyncApiTests
{
    [Fact]
    public async Task EqualRepositoryCompletesAsNoOp()
    {
        using var plugins = await GitTestRepository.CreateAsync();
        using var configs = await GitTestRepository.CreateAsync();
        using var factory = CreateFactory(plugins, configs);
        using var client = factory.CreateClient();
        var initialHead = await plugins.GetHeadAsync();

        using var response = await PostSyncAsync(client, "plugins");

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(initialHead, await plugins.GetHeadAsync());
        using var report = await GetReportAsync(client, response);
        Assert.Equal("gitSync", report.RootElement.GetProperty("type").GetString());
        Assert.Equal("none", report.RootElement.GetProperty("sync").GetProperty("behavior").GetString());
        Assert.Equal("equal", report.RootElement.GetProperty("sync").GetProperty("initialRelation").GetString());
    }

    [Fact]
    public async Task BehindRepositoryFastForwardsAfterFreshFetchWhileRustIsRunning()
    {
        using var plugins = await GitTestRepository.CreateAsync();
        using var configs = await GitTestRepository.CreateAsync();
        using var factory = CreateFactory(plugins, configs, serverIsRunning: true);
        using var client = factory.CreateClient();
        var remoteHead = await plugins.AddRemoteCommitAsync("remote.txt");

        using var response = await PostSyncAsync(client, "plugins");

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(remoteHead, await plugins.GetHeadAsync());
        using var report = await GetReportAsync(client, response);
        Assert.Equal("fastForward", report.RootElement.GetProperty("sync").GetProperty("behavior").GetString());
    }

    [Fact]
    public async Task AheadRepositoryPushesLocalCommits()
    {
        using var plugins = await GitTestRepository.CreateAsync();
        using var configs = await GitTestRepository.CreateAsync();
        using var factory = CreateFactory(plugins, configs);
        using var client = factory.CreateClient();
        var localHead = await plugins.AddWorkspaceCommitAsync("local.txt", "local"u8.ToArray());

        using var response = await PostSyncAsync(client, "plugins");

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(localHead, await plugins.GetRemoteHeadAsync("develop"));
        using var report = await GetReportAsync(client, response);
        Assert.Equal("push", report.RootElement.GetProperty("sync").GetProperty("behavior").GetString());
    }

    [Fact]
    public async Task DivergedRepositoryMergesAndPushesWithoutDiscardingLocalHistory()
    {
        using var plugins = await GitTestRepository.CreateAsync();
        using var configs = await GitTestRepository.CreateAsync();
        using var factory = CreateFactory(plugins, configs);
        using var client = factory.CreateClient();
        await plugins.AddRemoteCommitAsync("shared.txt", "base"u8.ToArray());
        await plugins.FetchOriginAsync();
        await plugins.ExecuteWorkspaceGitAsync("merge", "--ff-only", "origin/develop");
        await plugins.AddWorkspaceCommitAsync("local.txt", "local"u8.ToArray());
        await plugins.AddRemoteCommitAsync("remote.txt", "remote"u8.ToArray());

        using var response = await PostSyncAsync(client, "plugins");

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(await plugins.GetHeadAsync(), await plugins.GetRemoteHeadAsync("develop"));
        using var report = await GetReportAsync(client, response);
        Assert.Equal("mergeAndPush", report.RootElement.GetProperty("sync").GetProperty("behavior").GetString());
    }

    [Fact]
    public async Task ConflictedMergeIsAbortedAndRestoresOriginalHead()
    {
        using var plugins = await GitTestRepository.CreateAsync();
        using var configs = await GitTestRepository.CreateAsync();
        using var factory = CreateFactory(plugins, configs);
        using var client = factory.CreateClient();
        await plugins.AddRemoteCommitAsync("shared.txt", "base"u8.ToArray());
        await plugins.FetchOriginAsync();
        await plugins.ExecuteWorkspaceGitAsync("merge", "--ff-only", "origin/develop");
        var localHead = await plugins.AddWorkspaceCommitAsync("shared.txt", "local"u8.ToArray());
        await plugins.AddRemoteCommitAsync("shared.txt", "remote"u8.ToArray());

        using var response = await PostSyncAsync(client, "plugins");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(localHead, await plugins.GetHeadAsync());
        using var error = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("GitMergeConflict", error.RootElement.GetProperty("code").GetString());
        var operationId = error.RootElement.GetProperty("details").GetProperty("operationId").GetString();
        Assert.NotNull(operationId);
        using var reportResponse = await client.GetAsync($"/api/operations/{operationId}");
        reportResponse.EnsureSuccessStatusCode();
        using var report = JsonDocument.Parse(await reportResponse.Content.ReadAsStringAsync());
        Assert.Equal("succeeded", report.RootElement.GetProperty("rollback").GetProperty("status").GetString());
        Assert.False(report.RootElement.GetProperty("sync").GetProperty("manualInterventionRequired").GetBoolean());
    }

    [Fact]
    public async Task DirtyWorkspaceIsRejectedWithoutModification()
    {
        using var plugins = await GitTestRepository.CreateAsync();
        using var configs = await GitTestRepository.CreateAsync();
        using var factory = CreateFactory(plugins, configs);
        using var client = factory.CreateClient();
        var originalHead = await plugins.GetHeadAsync();
        await File.WriteAllTextAsync(Path.Combine(plugins.WorkspacePath, "pending.txt"), "pending");

        using var response = await PostSyncAsync(client, "plugins");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(originalHead, await plugins.GetHeadAsync());
        using var error = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("GitSyncWorkspaceUnsafe", error.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task DetachedUnsupportedAndMissingUpstreamBranchesAreRejected()
    {
        using var detachedPlugins = await GitTestRepository.CreateAsync();
        using var detachedConfigs = await GitTestRepository.CreateAsync();
        using (var detachedFactory = CreateFactory(detachedPlugins, detachedConfigs))
        using (var detachedClient = detachedFactory.CreateClient())
        {
            await detachedPlugins.ExecuteWorkspaceGitAsync("switch", "--detach");
            using var detachedResponse = await PostSyncAsync(detachedClient, "plugins");
            Assert.Equal(HttpStatusCode.Conflict, detachedResponse.StatusCode);
        }

        using var plugins = await GitTestRepository.CreateAsync();
        using var configs = await GitTestRepository.CreateAsync();
        using var factory = CreateFactory(plugins, configs);
        using var client = factory.CreateClient();
        await plugins.ExecuteWorkspaceGitAsync("branch", "--unset-upstream");

        using var response = await PostSyncAsync(client, "plugins");
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var error = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("GitSyncWorkspaceUnsafe", error.RootElement.GetProperty("code").GetString());

        using var unsupportedPlugins = await GitTestRepository.CreateAsync();
        using var unsupportedConfigs = await GitTestRepository.CreateAsync();
        using var unsupportedFactory = CreateFactory(unsupportedPlugins, unsupportedConfigs);
        using var unsupportedClient = unsupportedFactory.CreateClient();
        await unsupportedPlugins.ExecuteWorkspaceGitAsync("switch", "--create", "feature/test");

        using var unsupportedResponse = await PostSyncAsync(unsupportedClient, "plugins");
        Assert.Equal(HttpStatusCode.Conflict, unsupportedResponse.StatusCode);
    }

    [Fact]
    public async Task RemoteChangeBeforePushFailsWithoutForcePush()
    {
        using var plugins = await GitTestRepository.CreateAsync();
        using var configs = await GitTestRepository.CreateAsync();
        using var factory = CreateFactory(
            plugins,
            configs,
            services =>
            {
                services.RemoveAll<IRemoteGit>();
                services.AddSingleton<IRemoteGit>(serviceProvider =>
                    new RemoteChangesBeforePush(
                        new RemoteGit(
                            serviceProvider.GetRequiredService<IOptions<LocalGitOptions>>(),
                            serviceProvider.GetRequiredService<GitProcess>()),
                        plugins));
            });
        using var client = factory.CreateClient();
        var localHead = await plugins.AddWorkspaceCommitAsync("local.txt", "local"u8.ToArray());

        using var response = await PostSyncAsync(client, "plugins");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(localHead, await plugins.GetHeadAsync());
        Assert.NotEqual(localHead, await plugins.GetRemoteHeadAsync("develop"));
        using var error = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("GitRemoteChanged", error.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task FailedPushWithoutObservedRemoteChangeRemainsGitSyncFailed()
    {
        using var plugins = await GitTestRepository.CreateAsync();
        using var configs = await GitTestRepository.CreateAsync();
        using var factory = CreateFactory(
            plugins,
            configs,
            services =>
            {
                services.RemoveAll<IRemoteGit>();
                services.AddSingleton<IRemoteGit>(serviceProvider =>
                    new FailingPushRemoteGit(
                        new RemoteGit(
                            serviceProvider.GetRequiredService<IOptions<LocalGitOptions>>(),
                            serviceProvider.GetRequiredService<GitProcess>())));
            });
        using var client = factory.CreateClient();
        var localHead = await plugins.AddWorkspaceCommitAsync("local.txt", "local"u8.ToArray());

        using var response = await PostSyncAsync(client, "plugins");

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal(localHead, await plugins.GetHeadAsync());
        Assert.NotEqual(localHead, await plugins.GetRemoteHeadAsync("develop"));
        using var error = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("GitSyncFailed", error.RootElement.GetProperty("code").GetString());
    }

    private static PluginApplicationFactory CreateFactory(
        GitTestRepository plugins,
        GitTestRepository configs,
        Action<IServiceCollection>? configureServices = null,
        bool serverIsRunning = false) =>
        new(
            new Dictionary<string, string?>
            {
                ["PluginManagement:StateRootPath"] = Path.Combine(plugins.RootPath, "state"),
                ["PluginManagement:Repositories:Plugins:WorkspacePath"] = plugins.WorkspacePath,
                ["PluginManagement:Repositories:Configs:WorkspacePath"] = configs.WorkspacePath
            },
            serverIsRunning: serverIsRunning,
            configureServices: configureServices);

    private static Task<HttpResponseMessage> PostSyncAsync(HttpClient client, string repository) =>
        client.PostAsync(
            "/api/git/sync",
            new StringContent($"{{\"repository\":\"{repository}\"}}", Encoding.UTF8, "application/json"));

    private static async Task<JsonDocument> GetReportAsync(HttpClient client, HttpResponseMessage response)
    {
        using var operationResponse = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var operationId = operationResponse.RootElement.GetProperty("operationId").GetString();
        Assert.NotNull(operationId);
        var reportResponse = await client.GetAsync($"/api/operations/{operationId}");
        reportResponse.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await reportResponse.Content.ReadAsStringAsync());
    }

    private sealed class RemoteChangesBeforePush(IRemoteGit inner, GitTestRepository repository) : IRemoteGit
    {
        private bool changed;

        public Task<RemoteGitFetchResult> FetchAsync(RepositoryId repositoryId, CancellationToken cancellationToken) =>
            inner.FetchAsync(repositoryId, cancellationToken);

        public Task<RemoteGitRepositoryState> GetStateAsync(
            RepositoryId repositoryId,
            string? trackingBranch,
            CancellationToken cancellationToken) =>
            inner.GetStateAsync(repositoryId, trackingBranch, cancellationToken);

        public Task<RemoteGitReleaseBranchesResult> GetReleaseBranchesAsync(
            RepositoryId repositoryId,
            CancellationToken cancellationToken) => inner.GetReleaseBranchesAsync(repositoryId, cancellationToken);

        public async Task<RemoteGitPushResult> PushAsync(
            RepositoryId repositoryId,
            Source source,
            CancellationToken cancellationToken)
        {
            if (repositoryId is RepositoryId.Plugins && !changed)
            {
                changed = true;
                await repository.AddRemoteCommitAsync("race.txt", "remote"u8.ToArray());
            }

            return await inner.PushAsync(repositoryId, source, cancellationToken);
        }
    }

    private sealed class FailingPushRemoteGit(IRemoteGit inner) : IRemoteGit
    {
        public Task<RemoteGitFetchResult> FetchAsync(RepositoryId repository, CancellationToken cancellationToken) =>
            inner.FetchAsync(repository, cancellationToken);

        public Task<RemoteGitRepositoryState> GetStateAsync(
            RepositoryId repository,
            string? trackingBranch,
            CancellationToken cancellationToken) =>
            inner.GetStateAsync(repository, trackingBranch, cancellationToken);

        public Task<RemoteGitReleaseBranchesResult> GetReleaseBranchesAsync(
            RepositoryId repository,
            CancellationToken cancellationToken) =>
            inner.GetReleaseBranchesAsync(repository, cancellationToken);

        public Task<RemoteGitPushResult> PushAsync(
            RepositoryId repository,
            Source source,
            CancellationToken cancellationToken) =>
            Task.FromResult(new RemoteGitPushResult(repository, RemoteGitPushStatus.Failed));
    }
}
