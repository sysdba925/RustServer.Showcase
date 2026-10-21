using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using RustServer.Plugins.Features.PluginManagement.Baseline;
using RustServer.Plugins.Features.PluginManagement.Commands.FetchGit;
using RustServer.Plugins.Features.PluginManagement.Commands.GitSync;
using RustServer.Plugins.Features.PluginManagement.Git;
using RustServer.Plugins.Features.PluginManagement.Guards;
using RustServer.Plugins.Features.PluginManagement.LocalGit;
using RustServer.Plugins.Features.PluginManagement.Models;
using RustServer.Plugins.Features.PluginManagement.Models.Git;
using RustServer.Plugins.Features.PluginManagement.Models.Operations;
using RustServer.Plugins.Features.PluginManagement.Operations;
using RustServer.Plugins.Features.PluginManagement.RemoteGit;
using Xunit;

namespace RustServer.Plugins.Tests;

public sealed class FailureRecoveryApiTests
{
    [Fact]
    public async Task CommitReportsGitRepositoryUnavailableWhenBaselineSourceHeadCannotBeRead()
    {
        using var plugins = await GitTestRepository.CreateAsync();
        using var configs = await GitTestRepository.CreateAsync();
        using var runtimeRoot = new TemporaryRuntimeRoot();
        await plugins.AddRemoteCommitAsync("Example.cs", "baseline"u8.ToArray());
        var oxidePath = CreateRuntimeRoot(runtimeRoot.ServerRootPath);

        Baseline baseline;
        using (var setupFactory = CreateFactory(plugins, configs, runtimeRoot.ServerRootPath))
        using (var setupClient = setupFactory.CreateClient())
        {
            await ApplyDevelopmentAsync(setupClient);
            baseline = await GetBaselineAsync(setupFactory);
        }

        await File.WriteAllBytesAsync(
            Path.Combine(oxidePath, "plugins", "Example.cs"),
            "runtime change"u8.ToArray());
        using var factory = CreateFactory(
            plugins,
            configs,
            runtimeRoot.ServerRootPath,
            services =>
            {
                services.RemoveAll<ILocalGit>();
                services.AddSingleton<ILocalGit>(serviceProvider =>
                    new UnavailableSourceHeadLocalGit(new LocalGit(
                        serviceProvider.GetRequiredService<IOptions<LocalGitOptions>>(),
                        serviceProvider.GetRequiredService<GitProcess>())));
            });
        using var client = factory.CreateClient();

        using var response = await PostCommitAsync(client, "Must not misclassify Git unavailability");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var error = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("GitRepositoryUnavailable", error.RootElement.GetProperty("code").GetString());
        var operationId = error.RootElement.GetProperty("details").GetProperty("operationId").GetString();
        Assert.NotNull(operationId);
        Assert.Equal(baseline, await GetBaselineAsync(factory));

        using var reportResponse = await client.GetAsync($"/api/operations/{operationId}");
        reportResponse.EnsureSuccessStatusCode();
        using var report = JsonDocument.Parse(await reportResponse.Content.ReadAsStringAsync());
        Assert.Equal("failed", report.RootElement.GetProperty("status").GetString());
        Assert.Equal("GitRepositoryUnavailable", report.RootElement.GetProperty("failure").GetProperty("code").GetString());
    }

    [Fact]
    public async Task FetchCancellationDuringRemoteFetchPreservesTheActivePhaseAndFinalizesBeforeReleasingTheGuard()
    {
        using var plugins = await GitTestRepository.CreateAsync();
        using var configs = await GitTestRepository.CreateAsync();
        using var runtimeRoot = new TemporaryRuntimeRoot();
        CreateRuntimeRoot(runtimeRoot.ServerRootPath);
        using var cancellationSource = new CancellationTokenSource();
        using var factory = CreateFactory(
            plugins,
            configs,
            runtimeRoot.ServerRootPath,
            services =>
            {
                services.RemoveAll<IRemoteGit>();
                services.AddSingleton<IRemoteGit>(serviceProvider =>
                    new CancelingFetchRemoteGit(
                        new RemoteGit(
                            serviceProvider.GetRequiredService<IOptions<LocalGitOptions>>(),
                            serviceProvider.GetRequiredService<GitProcess>()),
                        cancellationSource));
            });
        var handler = factory.Services.GetRequiredService<FetchGitHandler>();

        var result = await handler.HandleAsync(cancellationSource.Token);

        Assert.Equal(FetchGitFailureReason.OperationCanceled, result.Failure?.Reason);
        Assert.NotNull(result.OperationId);
        var reports = factory.Services.GetRequiredService<IOperationReports>();
        var report = reports.Get(result.OperationId!);
        Assert.NotNull(report);
        Assert.Equal(OperationStatus.Failed, report.Status);
        Assert.Equal("OperationCanceled", report.Failure?.Code);
        Assert.Equal("fetchPlugins", report.Failure?.Phase);
        Assert.Equal(OperationStepStatus.Completed, GetStep(report, "captureFinalState").Status);

        var guard = factory.Services.GetRequiredService<IExclusiveMutationGuard>();
        var lease = guard.TryAcquire();
        Assert.NotNull(lease);
        await lease.DisposeAsync();
    }

    [Fact]
    public async Task SyncReportsManualInterventionWhenConflictedMergeCannotBeAborted()
    {
        using var plugins = await GitTestRepository.CreateAsync();
        using var configs = await GitTestRepository.CreateAsync();
        using var runtimeRoot = new TemporaryRuntimeRoot();
        CreateRuntimeRoot(runtimeRoot.ServerRootPath);
        await plugins.AddRemoteCommitAsync("shared.txt", "base"u8.ToArray());
        await plugins.FetchOriginAsync();
        await plugins.ExecuteWorkspaceGitAsync("merge", "--ff-only", "origin/develop");
        await plugins.AddWorkspaceCommitAsync("shared.txt", "local"u8.ToArray());
        await plugins.AddRemoteCommitAsync("shared.txt", "remote"u8.ToArray());
        using var factory = CreateFactory(
            plugins,
            configs,
            runtimeRoot.ServerRootPath,
            services =>
            {
                services.RemoveAll<ILocalGit>();
                services.AddSingleton<ILocalGit>(serviceProvider =>
                    new FailingMergeAbortLocalGit(
                        new LocalGit(
                            serviceProvider.GetRequiredService<IOptions<LocalGitOptions>>(),
                            serviceProvider.GetRequiredService<GitProcess>())));
            });
        using var client = factory.CreateClient();

        using var response = await client.PostAsync(
            "/api/git/sync",
            new StringContent("{\"repository\":\"plugins\"}", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        using var error = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("GitMergeRecoveryFailed", error.RootElement.GetProperty("code").GetString());
        var operationId = error.RootElement.GetProperty("details").GetProperty("operationId").GetString();
        Assert.NotNull(operationId);
        using var reportResponse = await client.GetAsync($"/api/operations/{operationId}");
        reportResponse.EnsureSuccessStatusCode();
        using var report = JsonDocument.Parse(await reportResponse.Content.ReadAsStringAsync());
        Assert.True(report.RootElement.GetProperty("sync").GetProperty("manualInterventionRequired").GetBoolean());
        Assert.Equal("failed", report.RootElement.GetProperty("rollback").GetProperty("status").GetString());
    }

    [Fact]
    public async Task SyncAbortsUnfinishedMergeWhenMergeReportsGenericFailure()
    {
        using var plugins = await GitTestRepository.CreateAsync();
        using var configs = await GitTestRepository.CreateAsync();
        using var runtimeRoot = new TemporaryRuntimeRoot();
        CreateRuntimeRoot(runtimeRoot.ServerRootPath);
        await plugins.AddRemoteCommitAsync("shared.txt", "base"u8.ToArray());
        await plugins.FetchOriginAsync();
        await plugins.ExecuteWorkspaceGitAsync("merge", "--ff-only", "origin/develop");
        var localHead = await plugins.AddWorkspaceCommitAsync("shared.txt", "local"u8.ToArray());
        await plugins.AddRemoteCommitAsync("shared.txt", "remote"u8.ToArray());
        using var factory = CreateFactory(
            plugins,
            configs,
            runtimeRoot.ServerRootPath,
            services =>
            {
                services.RemoveAll<ILocalGit>();
                services.AddSingleton<ILocalGit>(serviceProvider =>
                    new GenericFailureAfterConflictedMergeLocalGit(
                        new LocalGit(
                            serviceProvider.GetRequiredService<IOptions<LocalGitOptions>>(),
                            serviceProvider.GetRequiredService<GitProcess>())));
            });
        using var client = factory.CreateClient();

        using var response = await client.PostAsync(
            "/api/git/sync",
            new StringContent("{\"repository\":\"plugins\"}", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal(localHead, await plugins.GetHeadAsync());
        using var error = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("GitSyncFailed", error.RootElement.GetProperty("code").GetString());
        var operationId = error.RootElement.GetProperty("details").GetProperty("operationId").GetString();
        Assert.NotNull(operationId);
        using var reportResponse = await client.GetAsync($"/api/operations/{operationId}");
        reportResponse.EnsureSuccessStatusCode();
        using var report = JsonDocument.Parse(await reportResponse.Content.ReadAsStringAsync());
        Assert.Equal("succeeded", report.RootElement.GetProperty("rollback").GetProperty("status").GetString());
        Assert.False(report.RootElement.GetProperty("sync").GetProperty("manualInterventionRequired").GetBoolean());
    }

    [Fact]
    public async Task SyncCancellationDuringMergeAbortsUnfinishedMergeAndRestoresOriginalHead()
    {
        using var plugins = await GitTestRepository.CreateAsync();
        using var configs = await GitTestRepository.CreateAsync();
        using var runtimeRoot = new TemporaryRuntimeRoot();
        CreateRuntimeRoot(runtimeRoot.ServerRootPath);
        await plugins.AddRemoteCommitAsync("shared.txt", "base"u8.ToArray());
        await plugins.FetchOriginAsync();
        await plugins.ExecuteWorkspaceGitAsync("merge", "--ff-only", "origin/develop");
        var localHead = await plugins.AddWorkspaceCommitAsync("shared.txt", "local"u8.ToArray());
        await plugins.AddRemoteCommitAsync("shared.txt", "remote"u8.ToArray());
        using var cancellationSource = new CancellationTokenSource();
        using var factory = CreateFactory(
            plugins,
            configs,
            runtimeRoot.ServerRootPath,
            services =>
            {
                services.RemoveAll<ILocalGit>();
                services.AddSingleton<ILocalGit>(serviceProvider =>
                    new CancelingMergeLocalGit(
                        new LocalGit(
                            serviceProvider.GetRequiredService<IOptions<LocalGitOptions>>(),
                            serviceProvider.GetRequiredService<GitProcess>()),
                        cancellationSource));
            });
        var handler = factory.Services.GetRequiredService<GitSyncHandler>();

        var result = await handler.HandleAsync(
            new GitSyncCommand(RepositoryId.Plugins),
            cancellationSource.Token);

        Assert.Equal(GitSyncFailureReason.OperationCanceled, result.Failure?.Reason);
        Assert.Equal(localHead, await plugins.GetHeadAsync());
        var report = factory.Services.GetRequiredService<IOperationReports>().Get(result.OperationId!);
        Assert.NotNull(report);
        Assert.Equal(RollbackStatus.Succeeded, report.Rollback?.Status);
        Assert.False(report.Sync?.ManualInterventionRequired);
    }

    [Fact]
    public async Task ApplyReportsGitRepositoryUnavailableWhenSourceHeadCannotBeRead()
    {
        using var plugins = await GitTestRepository.CreateAsync();
        using var configs = await GitTestRepository.CreateAsync();
        using var runtimeRoot = new TemporaryRuntimeRoot();
        CreateRuntimeRoot(runtimeRoot.ServerRootPath);
        using var factory = CreateFactory(
            plugins,
            configs,
            runtimeRoot.ServerRootPath,
            services =>
            {
                services.RemoveAll<ILocalGit>();
                services.AddSingleton<ILocalGit>(serviceProvider =>
                    new UnavailableSourceHeadLocalGit(new LocalGit(
                        serviceProvider.GetRequiredService<IOptions<LocalGitOptions>>(),
                        serviceProvider.GetRequiredService<GitProcess>())));
            });
        using var client = factory.CreateClient();

        using var response = await client.PostAsync(
            "/api/source/apply",
            new StringContent("{\"source\":\"development\"}", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var error = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("GitRepositoryUnavailable", error.RootElement.GetProperty("code").GetString());
        Assert.NotEqual("TargetSourceUnavailable", error.RootElement.GetProperty("code").GetString());
        var operationId = error.RootElement.GetProperty("details").GetProperty("operationId").GetString();
        Assert.NotNull(operationId);

        using var reportResponse = await client.GetAsync($"/api/operations/{operationId}");
        reportResponse.EnsureSuccessStatusCode();
        using var report = JsonDocument.Parse(await reportResponse.Content.ReadAsStringAsync());
        Assert.Equal("failed", report.RootElement.GetProperty("status").GetString());
        Assert.Equal("GitRepositoryUnavailable", report.RootElement.GetProperty("failure").GetProperty("code").GetString());
        Assert.NotEqual(
            "TargetSourceUnavailable",
            report.RootElement.GetProperty("failure").GetProperty("code").GetString());
        Assert.Equal("completed", GetStep(report.RootElement.GetProperty("steps").EnumerateArray(), "captureFinalState")
            .GetProperty("status").GetString());
    }

    [Fact]
    public async Task DirtyWorkspaceBlocksRuntimeCommitWithoutAutomaticRepair()
    {
        using var plugins = await GitTestRepository.CreateAsync();
        using var configs = await GitTestRepository.CreateAsync();
        using var runtimeRoot = new TemporaryRuntimeRoot();
        await plugins.AddRemoteCommitAsync("Example.cs", "baseline"u8.ToArray());
        var oxidePath = CreateRuntimeRoot(runtimeRoot.ServerRootPath);

        Baseline baseline;
        var pluginsHeadBeforeCommit = string.Empty;
        using (var setupFactory = CreateFactory(plugins, configs, runtimeRoot.ServerRootPath))
        using (var setupClient = setupFactory.CreateClient())
        {
            await ApplyDevelopmentAsync(setupClient);
            baseline = await GetBaselineAsync(setupFactory);
            pluginsHeadBeforeCommit = await plugins.GetHeadAsync();
        }

        var dirtyWorkspaceFile = Path.Combine(plugins.WorkspacePath, "operator-note.txt");
        await File.WriteAllTextAsync(dirtyWorkspaceFile, "do not remove");
        await File.WriteAllBytesAsync(
            Path.Combine(oxidePath, "plugins", "Example.cs"),
            "runtime change"u8.ToArray());
        using var factory = CreateFactory(plugins, configs, runtimeRoot.ServerRootPath);
        using var client = factory.CreateClient();

        using var response = await PostCommitAsync(client, "Must be blocked by a dirty workspace");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var error = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("GitWorkspaceUnsafe", error.RootElement.GetProperty("code").GetString());
        Assert.True(File.Exists(dirtyWorkspaceFile));
        Assert.Equal("do not remove", await File.ReadAllTextAsync(dirtyWorkspaceFile));
        Assert.Equal(pluginsHeadBeforeCommit, await plugins.GetHeadAsync());
        Assert.Equal(baseline, await GetBaselineAsync(factory));
    }

    private static PluginApplicationFactory CreateFactory(
        GitTestRepository plugins,
        GitTestRepository configs,
        string runtimeServerRootPath,
        Action<IServiceCollection>? configureServices = null) =>
        new(new Dictionary<string, string?>
        {
            ["PluginManagement:StateRootPath"] = Path.Combine(plugins.RootPath, "state"),
            ["PluginManagement:Repositories:Plugins:WorkspacePath"] = plugins.WorkspacePath,
            ["PluginManagement:Repositories:Configs:WorkspacePath"] = configs.WorkspacePath,
            ["PluginManagement:Runtime:ServerRootPath"] = runtimeServerRootPath
        }, configureServices: configureServices);

    private static string CreateRuntimeRoot(string serverRootPath)
    {
        var oxidePath = Path.Combine(serverRootPath, "oxide");
        Directory.CreateDirectory(Path.Combine(oxidePath, "plugins"));
        Directory.CreateDirectory(Path.Combine(oxidePath, "config"));
        Directory.CreateDirectory(Path.Combine(oxidePath, "data"));
        return oxidePath;
    }

    private static async Task ApplyDevelopmentAsync(HttpClient client)
    {
        using var response = await client.PostAsync(
            "/api/source/apply",
            new StringContent("{\"source\":\"development\"}", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    private static async Task<Baseline> GetBaselineAsync(PluginApplicationFactory factory) =>
        await factory.Services.GetRequiredService<IBaselineStore>().GetAsync(CancellationToken.None) ??
        throw new InvalidOperationException("Expected Apply to persist a Baseline.");

    private static Task<HttpResponseMessage> PostCommitAsync(HttpClient client, string message) =>
        client.PostAsync(
            "/api/runtime/commit",
            new StringContent(
                JsonSerializer.Serialize(new
                {
                    message,
                    changes = new[]
                    {
                        new { repository = "plugins", path = "oxide/plugins/Example.cs" }
                    }
                }),
                Encoding.UTF8,
                "application/json"));

    private static OperationStep GetStep(OperationReport report, string stepName) =>
        Assert.Single(report.Steps, step => step.Name == stepName);

    private static JsonElement GetStep(IEnumerable<JsonElement> steps, string stepName) =>
        steps.Single(step => string.Equals(step.GetProperty("name").GetString(), stepName, StringComparison.Ordinal));

    private sealed class CancelingFetchRemoteGit(
        IRemoteGit inner,
        CancellationTokenSource cancellationSource) : IRemoteGit
    {
        public Task<RemoteGitFetchResult> FetchAsync(
            RepositoryId repository,
            CancellationToken cancellationToken)
        {
            if (repository is RepositoryId.Plugins)
            {
                cancellationSource.Cancel();
                cancellationToken.ThrowIfCancellationRequested();
            }

            return inner.FetchAsync(repository, cancellationToken);
        }

        public Task<RemoteGitRepositoryState> GetStateAsync(
            RepositoryId repository,
            string? trackingBranch,
            CancellationToken cancellationToken) =>
            inner.GetStateAsync(repository, trackingBranch, cancellationToken);

        public Task<RemoteGitReleaseBranchesResult> GetReleaseBranchesAsync(
            RepositoryId repository,
            CancellationToken cancellationToken) => inner.GetReleaseBranchesAsync(repository, cancellationToken);

        public Task<RemoteGitPushResult> PushAsync(
            RepositoryId repository,
            Source source,
            CancellationToken cancellationToken) => inner.PushAsync(repository, source, cancellationToken);
    }

    private sealed class UnavailableSourceHeadLocalGit(ILocalGit inner) : DelegatingLocalGit(inner)
    {
        public override Task<GitCommitSha?> GetKnownRemoteSourceHeadAsync(
            RepositoryId repository,
            Source source,
            CancellationToken cancellationToken) =>
            throw new LocalGitException("Git CLI is unavailable while reading the Source head.");
    }

    private sealed class FailingMergeAbortLocalGit(ILocalGit inner) : DelegatingLocalGit(inner)
    {
        public override Task AbortMergeAsync(
            RepositoryId repository,
            LocalGitRepositoryState originalState,
            CancellationToken cancellationToken) =>
            throw new LocalGitException("Configured merge abort failure.");
    }

    private sealed class GenericFailureAfterConflictedMergeLocalGit(ILocalGit inner) : DelegatingLocalGit(inner)
    {
        public override async Task<LocalGitMergeResult> MergeCurrentBranchWithUpstreamAsync(
            RepositoryId repository,
            string authorName,
            string authorEmail,
            CancellationToken cancellationToken)
        {
            await Inner.MergeCurrentBranchWithUpstreamAsync(
                repository,
                authorName,
                authorEmail,
                cancellationToken);
            return new LocalGitMergeResult(LocalGitMergeStatus.Failed);
        }
    }

    private sealed class CancelingMergeLocalGit(
        ILocalGit inner,
        CancellationTokenSource cancellationSource) : DelegatingLocalGit(inner)
    {
        public override async Task<LocalGitMergeResult> MergeCurrentBranchWithUpstreamAsync(
            RepositoryId repository,
            string authorName,
            string authorEmail,
            CancellationToken cancellationToken)
        {
            await Inner.MergeCurrentBranchWithUpstreamAsync(
                repository,
                authorName,
                authorEmail,
                cancellationToken);
            cancellationSource.Cancel();
            cancellationToken.ThrowIfCancellationRequested();
            throw new InvalidOperationException("Cancellation did not interrupt the merge.");
        }
    }

    private class DelegatingLocalGit(ILocalGit inner) : ILocalGit
    {
        protected ILocalGit Inner { get; } = inner;

        public virtual Task<IReadOnlySet<string>> GetIgnoredPathsAsync(
            RepositoryId repository,
            IReadOnlyList<string> repositoryRelativePaths,
            CancellationToken cancellationToken) =>
            Inner.GetIgnoredPathsAsync(repository, repositoryRelativePaths, cancellationToken);

        public virtual Task<LocalGitRepositoryState> GetStateAsync(
            RepositoryId repository,
            CancellationToken cancellationToken) => Inner.GetStateAsync(repository, cancellationToken);

        public virtual Task<LocalGitDevelopmentUpdateResult> TryFastForwardDevelopmentAsync(
            RepositoryId repository,
            CancellationToken cancellationToken) => Inner.TryFastForwardDevelopmentAsync(repository, cancellationToken);

        public virtual Task FastForwardCurrentBranchAsync(
            RepositoryId repository,
            CancellationToken cancellationToken) => Inner.FastForwardCurrentBranchAsync(repository, cancellationToken);

        public virtual Task<LocalGitMergeResult> MergeCurrentBranchWithUpstreamAsync(
            RepositoryId repository,
            string authorName,
            string authorEmail,
            CancellationToken cancellationToken) =>
            Inner.MergeCurrentBranchWithUpstreamAsync(repository, authorName, authorEmail, cancellationToken);

        public virtual Task<bool> IsMergeInProgressAsync(
            RepositoryId repository,
            CancellationToken cancellationToken) =>
            Inner.IsMergeInProgressAsync(repository, cancellationToken);

        public virtual Task AbortMergeAsync(
            RepositoryId repository,
            LocalGitRepositoryState originalState,
            CancellationToken cancellationToken) =>
            Inner.AbortMergeAsync(repository, originalState, cancellationToken);

        public virtual Task<GitCommitSha?> GetKnownRemoteSourceHeadAsync(
            RepositoryId repository,
            Source source,
            CancellationToken cancellationToken) =>
            Inner.GetKnownRemoteSourceHeadAsync(repository, source, cancellationToken);

        public virtual Task<LocalGitSourcePreparation> ValidateSourcePreparationAsync(
            RepositoryId repository,
            Source source,
            GitCommitSha targetHead,
            CancellationToken cancellationToken) =>
            Inner.ValidateSourcePreparationAsync(repository, source, targetHead, cancellationToken);

        public virtual Task<LocalGitSourcePreparation> PrepareSourceAsync(
            RepositoryId repository,
            Source source,
            GitCommitSha targetHead,
            CancellationToken cancellationToken) =>
            Inner.PrepareSourceAsync(repository, source, targetHead, cancellationToken);

        public virtual Task<LocalGitSourcePreparation> PrepareSourceWithoutSynchronizationAsync(
            RepositoryId repository,
            Source source,
            GitCommitSha targetHead,
            CancellationToken cancellationToken) =>
            Inner.PrepareSourceWithoutSynchronizationAsync(repository, source, targetHead, cancellationToken);

        public virtual Task RestoreSourcePreparationAsync(
            RepositoryId repository,
            LocalGitRepositoryState previousState,
            LocalGitSourcePreparation preparation,
            CancellationToken cancellationToken) =>
            Inner.RestoreSourcePreparationAsync(repository, previousState, preparation, cancellationToken);

        public virtual Task<IReadOnlyList<string>> ListFilesAtCommitAsync(
            RepositoryId repository,
            GitCommitSha commit,
            CancellationToken cancellationToken) =>
            Inner.ListFilesAtCommitAsync(repository, commit, cancellationToken);

        public virtual Task<byte[]> ReadFileAtCommitAsync(
            RepositoryId repository,
            GitCommitSha commit,
            string repositoryRelativePath,
            CancellationToken cancellationToken) =>
            Inner.ReadFileAtCommitAsync(repository, commit, repositoryRelativePath, cancellationToken);

        public virtual Task WriteWorkingFileAsync(
            RepositoryId repository,
            string repositoryRelativePath,
            ReadOnlyMemory<byte> content,
            CancellationToken cancellationToken) =>
            Inner.WriteWorkingFileAsync(repository, repositoryRelativePath, content, cancellationToken);

        public virtual Task RemoveWorkingFileAsync(
            RepositoryId repository,
            string repositoryRelativePath,
            CancellationToken cancellationToken) =>
            Inner.RemoveWorkingFileAsync(repository, repositoryRelativePath, cancellationToken);

        public virtual Task StageWorkingFileAsync(
            RepositoryId repository,
            string repositoryRelativePath,
            CancellationToken cancellationToken) =>
            Inner.StageWorkingFileAsync(repository, repositoryRelativePath, cancellationToken);

        public virtual Task<LocalGitStagingInspection> InspectStagingAsync(
            RepositoryId repository,
            CancellationToken cancellationToken) =>
            Inner.InspectStagingAsync(repository, cancellationToken);

        public virtual Task<GitCommitSha> CreateCommitAsync(
            RepositoryId repository,
            LocalGitCommit commit,
            CancellationToken cancellationToken) =>
            Inner.CreateCommitAsync(repository, commit, cancellationToken);

        public virtual Task RestoreOperationChangesAsync(
            RepositoryId repository,
            IReadOnlyList<LocalGitWorkingChange> changes,
            CancellationToken cancellationToken) =>
            Inner.RestoreOperationChangesAsync(repository, changes, cancellationToken);

        public virtual Task RestoreCreatedCommitAsync(
            RepositoryId repository,
            Source source,
            GitCommitSha previousHead,
            GitCommitSha createdCommit,
            CancellationToken cancellationToken) =>
            Inner.RestoreCreatedCommitAsync(repository, source, previousHead, createdCommit, cancellationToken);
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
