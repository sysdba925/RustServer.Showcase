using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using RustServer.Plugins.Features.PluginManagement;
using RustServer.Plugins.Features.PluginManagement.Baseline;
using RustServer.Plugins.Features.PluginManagement.Git;
using RustServer.Plugins.Features.PluginManagement.LocalGit;
using RustServer.Plugins.Features.PluginManagement.Models;
using RustServer.Plugins.Features.PluginManagement.Models.Git;
using RustServer.Plugins.Features.PluginManagement.RemoteGit;
using Xunit;

namespace RustServer.Plugins.Tests;

public sealed class RuntimeCommitApiTests
{
    [Fact]
    public async Task IgnoredRuntimeChangeCannotBeSelectedForCommit()
    {
        using var plugins = await GitTestRepository.CreateAsync();
        using var configs = await GitTestRepository.CreateAsync();
        using var runtimeRoot = new TemporaryRuntimeRoot();
        await configs.AddRemoteCommitAsync(".gitignore", "data/*\n"u8.ToArray());
        CreateRuntimeRoot(runtimeRoot.ServerRootPath);
        var oxideRoot = Path.Combine(runtimeRoot.ServerRootPath, "oxide");

        using var factory = CreateFactory(plugins, configs, runtimeRoot.ServerRootPath);
        using var client = factory.CreateClient();
        await ApplyDevelopmentAsync(client);
        var before = await GetBaselineAsync(factory);
        var ignoredPath = Path.Combine(oxideRoot, "data", "oxide.users.data");
        await File.WriteAllTextAsync(ignoredPath, "keep");

        using var response = await PostCommitAsync(
            client,
            "Must never stage ignored data",
            ("configs", "oxide/data/oxide.users.data"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var error = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("InvalidRuntimeSelection", error.RootElement.GetProperty("code").GetString());
        Assert.Equal(before, await GetBaselineAsync(factory));
        Assert.Equal("keep", await File.ReadAllTextAsync(ignoredPath));
    }

    [Fact]
    public async Task CommitsAndPushesSelectedRuntimeChangesAndLeavesOnlyUnselectedChangesInRuntimeDiff()
    {
        using var plugins = await GitTestRepository.CreateAsync();
        using var configs = await GitTestRepository.CreateAsync();
        using var runtimeRoot = new TemporaryRuntimeRoot();
        var pluginBaseline = "baseline plugin"u8.ToArray();
        var configBaseline = new byte[] { 0x00, 0xFF, 0x01 };
        var unselectedBaseline = "baseline unselected"u8.ToArray();
        await plugins.AddRemoteCommitAsync("Example.cs", pluginBaseline);
        await configs.AddRemoteCommitAsync("data/State.bin", configBaseline);
        await configs.AddRemoteCommitAsync("config/Unselected.json", unselectedBaseline);
        CreateRuntimeRoot(runtimeRoot.ServerRootPath);

        const string authorName = "Configured Runtime Author";
        const string authorEmail = "runtime.author@example.test";
        using var factory = CreateFactory(
            plugins,
            configs,
            runtimeRoot.ServerRootPath,
            configurationOverrides: new Dictionary<string, string?>
            {
                ["PluginManagement:GitCommitAuthor:Name"] = authorName,
                ["PluginManagement:GitCommitAuthor:Email"] = authorEmail
            });
        using var client = factory.CreateClient();
        await ApplyDevelopmentAsync(client);
        var baseline = await GetBaselineAsync(factory);
        var pluginRuntime = "selected plugin runtime"u8.ToArray();
        var configRuntime = new byte[] { 0xC3, 0x28, 0x00, 0xFF };
        var unselectedRuntime = "runtime remains unselected"u8.ToArray();
        var oxidePath = Path.Combine(runtimeRoot.ServerRootPath, "oxide");
        await File.WriteAllBytesAsync(Path.Combine(oxidePath, "plugins", "Example.cs"), pluginRuntime);
        await File.WriteAllBytesAsync(Path.Combine(oxidePath, "data", "State.bin"), configRuntime);
        await File.WriteAllBytesAsync(Path.Combine(oxidePath, "config", "Unselected.json"), unselectedRuntime);

        using var commitResponse = await PostCommitAsync(
            client,
            "Persist selected Runtime changes",
            ("plugins", "oxide/plugins/Example.cs"),
            ("configs", "oxide/data/State.bin"));

        Assert.Equal(HttpStatusCode.Accepted, commitResponse.StatusCode);
        var operationId = await GetOperationIdAsync(commitResponse);
        Assert.Equal(pluginRuntime, await plugins.GetRemoteFileAsync("develop", "Example.cs"));
        Assert.Equal(configRuntime, await configs.GetRemoteFileAsync("develop", "data/State.bin"));
        Assert.Equal(unselectedBaseline, await configs.GetRemoteFileAsync("develop", "config/Unselected.json"));
        Assert.Equal(await plugins.GetRemoteHeadAsync("develop"), await plugins.GetHeadAsync());
        Assert.Equal(await configs.GetRemoteHeadAsync("develop"), await configs.GetHeadAsync());
        var updatedBaseline = await GetBaselineAsync(factory);
        Assert.Equal(await plugins.GetRemoteHeadAsync("develop"), updatedBaseline.PluginsCommit.Value);
        Assert.Equal(await configs.GetRemoteHeadAsync("develop"), updatedBaseline.ConfigsCommit.Value);
        Assert.NotEqual(baseline.PluginsCommit, updatedBaseline.PluginsCommit);
        Assert.NotEqual(baseline.ConfigsCommit, updatedBaseline.ConfigsCommit);
        Assert.Equal(pluginRuntime, await File.ReadAllBytesAsync(Path.Combine(oxidePath, "plugins", "Example.cs")));
        Assert.Equal(configRuntime, await File.ReadAllBytesAsync(Path.Combine(oxidePath, "data", "State.bin")));
        Assert.Equal(unselectedRuntime, await File.ReadAllBytesAsync(Path.Combine(oxidePath, "config", "Unselected.json")));

        using var diffResponse = await client.GetAsync("/api/runtime/diff");
        diffResponse.EnsureSuccessStatusCode();
        using var diff = JsonDocument.Parse(await diffResponse.Content.ReadAsStringAsync());
        var remainingChanges = diff.RootElement.GetProperty("changes").EnumerateArray().ToArray();
        Assert.Single(remainingChanges);
        Assert.Equal("configs", remainingChanges[0].GetProperty("repository").GetString());
        Assert.Equal("oxide/config/Unselected.json", remainingChanges[0].GetProperty("path").GetString());
        Assert.Equal("modified", remainingChanges[0].GetProperty("change").GetString());

        var expectedCommitMessage = $"Persist selected Runtime changes\n\n" +
            $"RustServer-Operation: {operationId}\n" +
            "RustServer-Environment: staging\n" +
            "RustServer-Source: development";
        AssertCommitMetadata(
            await plugins.GetRemoteCommitInfoAsync("develop"),
            authorName,
            authorEmail,
            expectedCommitMessage);
        AssertCommitMetadata(
            await configs.GetRemoteCommitInfoAsync("develop"),
            authorName,
            authorEmail,
            expectedCommitMessage);

        using var reportResponse = await client.GetAsync($"/api/operations/{operationId}");
        reportResponse.EnsureSuccessStatusCode();
        using var report = JsonDocument.Parse(await reportResponse.Content.ReadAsStringAsync());
        Assert.Equal("commitRuntime", report.RootElement.GetProperty("type").GetString());
        Assert.Equal("succeeded", report.RootElement.GetProperty("status").GetString());
        Assert.Equal(2, report.RootElement.GetProperty("changes").GetArrayLength());
        Assert.Equal(baseline.PluginsCommit.Value, report.RootElement.GetProperty("previousBaseline").GetProperty("pluginsCommit").GetString());
        Assert.Equal(updatedBaseline.PluginsCommit.Value, report.RootElement.GetProperty("finalBaseline").GetProperty("pluginsCommit").GetString());
        Assert.Equal(updatedBaseline.ConfigsCommit.Value, report.RootElement.GetProperty("finalBaseline").GetProperty("configsCommit").GetString());
        Assert.All(
            report.RootElement.GetProperty("steps").EnumerateArray(),
            step => Assert.Equal("completed", step.GetProperty("status").GetString()));
    }

    [Fact]
    public async Task AdvancesOnlyTheAffectedRepositoryBaselineAndMakesCommittedRuntimeUpToDate()
    {
        using var plugins = await GitTestRepository.CreateAsync();
        using var configs = await GitTestRepository.CreateAsync();
        using var runtimeRoot = new TemporaryRuntimeRoot();
        await plugins.AddRemoteCommitAsync("Example.cs", "baseline plugin"u8.ToArray());
        CreateRuntimeRoot(runtimeRoot.ServerRootPath);

        using var factory = CreateFactory(plugins, configs, runtimeRoot.ServerRootPath);
        using var client = factory.CreateClient();
        await ApplyDevelopmentAsync(client);
        var baseline = await GetBaselineAsync(factory);
        var runtimeContent = "committed runtime change"u8.ToArray();
        await File.WriteAllBytesAsync(
            Path.Combine(runtimeRoot.ServerRootPath, "oxide", "plugins", "Example.cs"),
            runtimeContent);

        using var commitResponse = await PostCommitAsync(
            client,
            "Commit the complete Runtime Diff",
            ("plugins", "oxide/plugins/Example.cs"));

        Assert.Equal(HttpStatusCode.Accepted, commitResponse.StatusCode);
        var updatedBaseline = await GetBaselineAsync(factory);
        Assert.Equal(await plugins.GetRemoteHeadAsync("develop"), updatedBaseline.PluginsCommit.Value);
        Assert.Equal(baseline.ConfigsCommit, updatedBaseline.ConfigsCommit);

        using var diffResponse = await client.GetAsync("/api/runtime/diff");
        diffResponse.EnsureSuccessStatusCode();
        using var diff = JsonDocument.Parse(await diffResponse.Content.ReadAsStringAsync());
        Assert.Equal("ready", diff.RootElement.GetProperty("status").GetString());
        Assert.Empty(diff.RootElement.GetProperty("changes").EnumerateArray());
    }

    [Fact]
    public async Task AddsReleaseSourceTrailerToRuntimeCommit()
    {
        using var plugins = await GitTestRepository.CreateAsync();
        using var configs = await GitTestRepository.CreateAsync();
        using var runtimeRoot = new TemporaryRuntimeRoot();
        await plugins.AddRemoteCommitAsync("Example.cs", "release baseline"u8.ToArray());
        await plugins.AddRemoteReleaseAsync("2.4");
        await configs.AddRemoteReleaseAsync("2.4");
        CreateRuntimeRoot(runtimeRoot.ServerRootPath);

        using var factory = CreateFactory(plugins, configs, runtimeRoot.ServerRootPath);
        using var client = factory.CreateClient();
        await ApplySourceAsync(client, "release", "2.4");
        await File.WriteAllTextAsync(
            Path.Combine(runtimeRoot.ServerRootPath, "oxide", "plugins", "Example.cs"),
            "release runtime update");

        using var commitResponse = await PostCommitAsync(
            client,
            "Fix GatherManager config",
            ("plugins", "oxide/plugins/Example.cs"));

        Assert.Equal(HttpStatusCode.Accepted, commitResponse.StatusCode);
        var operationId = await GetOperationIdAsync(commitResponse);
        var expectedCommitMessage = $"Fix GatherManager config\n\n" +
            $"RustServer-Operation: {operationId}\n" +
            "RustServer-Environment: staging\n" +
            "RustServer-Source: release/2.4";
        AssertCommitMetadata(
            await plugins.GetRemoteCommitInfoAsync("release/2.4"),
            "Rust Server Platform",
            "rustserver@local",
            expectedCommitMessage);
    }

    [Fact]
    public async Task BlocksCommitWhenFreshFetchFindsSourceChangedSinceBaseline()
    {
        using var plugins = await GitTestRepository.CreateAsync();
        using var configs = await GitTestRepository.CreateAsync();
        using var runtimeRoot = new TemporaryRuntimeRoot();
        await plugins.AddRemoteCommitAsync("Example.cs", "baseline"u8.ToArray());
        CreateRuntimeRoot(runtimeRoot.ServerRootPath);

        using var factory = CreateFactory(plugins, configs, runtimeRoot.ServerRootPath);
        using var client = factory.CreateClient();
        await ApplyDevelopmentAsync(client);
        var baseline = await GetBaselineAsync(factory);
        var oxidePluginPath = Path.Combine(runtimeRoot.ServerRootPath, "oxide", "plugins", "Example.cs");
        var runtimeContent = "runtime delta"u8.ToArray();
        await File.WriteAllBytesAsync(oxidePluginPath, runtimeContent);
        var remoteHead = await plugins.AddRemoteCommitAsync("Example.cs", "remote delta"u8.ToArray());

        using var commitResponse = await PostCommitAsync(
            client,
            "Must be blocked",
            ("plugins", "oxide/plugins/Example.cs"));

        Assert.Equal(HttpStatusCode.Conflict, commitResponse.StatusCode);
        using var error = JsonDocument.Parse(await commitResponse.Content.ReadAsStringAsync());
        Assert.Equal("GitSourceChangedSinceBaseline", error.RootElement.GetProperty("code").GetString());
        var operationId = error.RootElement.GetProperty("details").GetProperty("operationId").GetString();
        Assert.NotNull(operationId);
        Assert.Equal(remoteHead, await plugins.GetRemoteHeadAsync("develop"));
        Assert.Equal(baseline.PluginsCommit.Value, await plugins.GetHeadAsync());
        Assert.Equal(baseline, await GetBaselineAsync(factory));
        Assert.Equal(runtimeContent, await File.ReadAllBytesAsync(oxidePluginPath));

        using var reportResponse = await client.GetAsync($"/api/operations/{operationId}");
        reportResponse.EnsureSuccessStatusCode();
        using var report = JsonDocument.Parse(await reportResponse.Content.ReadAsStringAsync());
        Assert.Equal("failed", report.RootElement.GetProperty("status").GetString());
        Assert.Equal("GitSourceChangedSinceBaseline", report.RootElement.GetProperty("failure").GetProperty("code").GetString());
    }

    [Fact]
    public async Task RejectsUnsynchronizedLocalBaselineSourceWithoutFastForwardingIt()
    {
        using var plugins = await GitTestRepository.CreateAsync();
        using var configs = await GitTestRepository.CreateAsync();
        using var runtimeRoot = new TemporaryRuntimeRoot();
        await plugins.AddRemoteCommitAsync("Example.cs", "baseline"u8.ToArray());
        CreateRuntimeRoot(runtimeRoot.ServerRootPath);
        using var factory = CreateFactory(plugins, configs, runtimeRoot.ServerRootPath);
        using var client = factory.CreateClient();
        await ApplyDevelopmentAsync(client);
        var baseline = await GetBaselineAsync(factory);
        await File.WriteAllTextAsync(
            Path.Combine(runtimeRoot.ServerRootPath, "oxide", "plugins", "Example.cs"),
            "runtime delta");
        await plugins.ExecuteWorkspaceGitAsync("reset", "--hard", "HEAD~1");
        var localHeadBeforeCommit = await plugins.GetHeadAsync();

        using var commitResponse = await PostCommitAsync(
            client,
            "Must not fast-forward implicitly",
            ("plugins", "oxide/plugins/Example.cs"));

        Assert.Equal(HttpStatusCode.Conflict, commitResponse.StatusCode);
        using var error = JsonDocument.Parse(await commitResponse.Content.ReadAsStringAsync());
        Assert.Equal("GitSynchronizationRequired", error.RootElement.GetProperty("code").GetString());
        Assert.Equal(localHeadBeforeCommit, await plugins.GetHeadAsync());
        Assert.Equal(baseline, await GetBaselineAsync(factory));
    }

    [Fact]
    public async Task ReportsPartialPushAndRemovesTheUnpushedRepositoryCommit()
    {
        using var plugins = await GitTestRepository.CreateAsync();
        using var configs = await GitTestRepository.CreateAsync();
        using var runtimeRoot = new TemporaryRuntimeRoot();
        await plugins.AddRemoteCommitAsync("Example.cs", "baseline plugin"u8.ToArray());
        var configsBaselineHead = await configs.AddRemoteCommitAsync("config/Example.json", "{}"u8.ToArray());
        CreateRuntimeRoot(runtimeRoot.ServerRootPath);

        using var factory = CreateFactory(
            plugins,
            configs,
            runtimeRoot.ServerRootPath,
            services =>
            {
                services.RemoveAll<IRemoteGit>();
                services.AddSingleton<IRemoteGit>(serviceProvider =>
                    new FailConfigsPushRemoteGit(new RemoteGit(
                        serviceProvider.GetRequiredService<IOptions<LocalGitOptions>>(),
                        serviceProvider.GetRequiredService<GitProcess>())));
            });
        using var client = factory.CreateClient();
        await ApplyDevelopmentAsync(client);
        var baseline = await GetBaselineAsync(factory);
        var oxidePath = Path.Combine(runtimeRoot.ServerRootPath, "oxide");
        var pluginRuntime = "plugin partial push"u8.ToArray();
        var configRuntime = "config partial push"u8.ToArray();
        await File.WriteAllBytesAsync(Path.Combine(oxidePath, "plugins", "Example.cs"), pluginRuntime);
        await File.WriteAllBytesAsync(Path.Combine(oxidePath, "config", "Example.json"), configRuntime);

        using var commitResponse = await PostCommitAsync(
            client,
            "Partial push must be explicit",
            ("plugins", "oxide/plugins/Example.cs"),
            ("configs", "oxide/config/Example.json"));

        Assert.Equal(HttpStatusCode.BadGateway, commitResponse.StatusCode);
        using var error = JsonDocument.Parse(await commitResponse.Content.ReadAsStringAsync());
        Assert.Equal("GitPushFailed", error.RootElement.GetProperty("code").GetString());
        var operationId = error.RootElement.GetProperty("details").GetProperty("operationId").GetString();
        Assert.NotNull(operationId);
        Assert.Equal(pluginRuntime, await plugins.GetRemoteFileAsync("develop", "Example.cs"));
        Assert.Equal(configsBaselineHead, await configs.GetRemoteHeadAsync("develop"));
        Assert.Equal(configsBaselineHead, await configs.GetHeadAsync());
        Assert.Equal(baseline, await GetBaselineAsync(factory));
        Assert.Equal(pluginRuntime, await File.ReadAllBytesAsync(Path.Combine(oxidePath, "plugins", "Example.cs")));
        Assert.Equal(configRuntime, await File.ReadAllBytesAsync(Path.Combine(oxidePath, "config", "Example.json")));

        using var reportResponse = await client.GetAsync($"/api/operations/{operationId}");
        reportResponse.EnsureSuccessStatusCode();
        using var report = JsonDocument.Parse(await reportResponse.Content.ReadAsStringAsync());
        Assert.Equal("failed", report.RootElement.GetProperty("status").GetString());
        Assert.Equal("GitPushFailed", report.RootElement.GetProperty("failure").GetProperty("code").GetString());
        Assert.Equal("completed", GetStep(report.RootElement, "pushPlugins").GetProperty("status").GetString());
        Assert.Equal("failed", GetStep(report.RootElement, "pushConfigs").GetProperty("status").GetString());
    }

    [Fact]
    public async Task DoesNotAdvanceBaselineWhenPersistenceFailsAfterSuccessfulPush()
    {
        using var plugins = await GitTestRepository.CreateAsync();
        using var configs = await GitTestRepository.CreateAsync();
        using var runtimeRoot = new TemporaryRuntimeRoot();
        await plugins.AddRemoteCommitAsync("Example.cs", "baseline plugin"u8.ToArray());
        CreateRuntimeRoot(runtimeRoot.ServerRootPath);

        using var factory = CreateFactory(
            plugins,
            configs,
            runtimeRoot.ServerRootPath,
            services =>
            {
                services.RemoveAll<IBaselineStore>();
                services.AddSingleton<IBaselineStore>(serviceProvider =>
                    new FailAfterInitialBaselineSaveStore(
                        new FileBaselineStore(
                            serviceProvider.GetRequiredService<IOptions<PluginManagementStorageOptions>>())));
            });
        using var client = factory.CreateClient();
        await ApplyDevelopmentAsync(client);
        var baseline = await GetBaselineAsync(factory);
        var runtimeContent = "runtime change"u8.ToArray();
        await File.WriteAllBytesAsync(
            Path.Combine(runtimeRoot.ServerRootPath, "oxide", "plugins", "Example.cs"),
            runtimeContent);

        using var commitResponse = await PostCommitAsync(
            client,
            "Baseline persistence must be reliable",
            ("plugins", "oxide/plugins/Example.cs"));

        Assert.Equal(HttpStatusCode.InternalServerError, commitResponse.StatusCode);
        using var error = JsonDocument.Parse(await commitResponse.Content.ReadAsStringAsync());
        Assert.Equal("BaselinePersistFailed", error.RootElement.GetProperty("code").GetString());
        var operationId = error.RootElement.GetProperty("details").GetProperty("operationId").GetString();
        Assert.NotNull(operationId);
        Assert.Equal(runtimeContent, await plugins.GetRemoteFileAsync("develop", "Example.cs"));
        Assert.Equal(baseline, await GetBaselineAsync(factory));

        using var reportResponse = await client.GetAsync($"/api/operations/{operationId}");
        reportResponse.EnsureSuccessStatusCode();
        using var report = JsonDocument.Parse(await reportResponse.Content.ReadAsStringAsync());
        Assert.Equal("failed", report.RootElement.GetProperty("status").GetString());
        Assert.Equal("BaselinePersistFailed", report.RootElement.GetProperty("failure").GetProperty("code").GetString());
        Assert.Equal("completed", GetStep(report.RootElement, "pushPlugins").GetProperty("status").GetString());
        Assert.Equal("failed", GetStep(report.RootElement, "persistBaseline").GetProperty("status").GetString());
        Assert.Equal(baseline.PluginsCommit.Value, report.RootElement.GetProperty("finalBaseline").GetProperty("pluginsCommit").GetString());
    }

    private static PluginApplicationFactory CreateFactory(
        GitTestRepository plugins,
        GitTestRepository configs,
        string runtimeServerRootPath,
        Action<IServiceCollection>? configureServices = null,
        IReadOnlyDictionary<string, string?>? configurationOverrides = null)
    {
        var configuration = new Dictionary<string, string?>
        {
            ["PluginManagement:StateRootPath"] = Path.Combine(plugins.RootPath, "state"),
            ["PluginManagement:Repositories:Plugins:WorkspacePath"] = plugins.WorkspacePath,
            ["PluginManagement:Repositories:Configs:WorkspacePath"] = configs.WorkspacePath,
            ["PluginManagement:Runtime:ServerRootPath"] = runtimeServerRootPath
        };

        if (configurationOverrides is not null)
        {
            foreach (var (key, value) in configurationOverrides)
            {
                configuration[key] = value;
            }
        }

        return new PluginApplicationFactory(configuration, configureServices: configureServices);
    }

    private static void CreateRuntimeRoot(string serverRootPath)
    {
        var oxidePath = Path.Combine(serverRootPath, "oxide");
        Directory.CreateDirectory(Path.Combine(oxidePath, "plugins"));
        Directory.CreateDirectory(Path.Combine(oxidePath, "config"));
        Directory.CreateDirectory(Path.Combine(oxidePath, "data"));
    }

    private static async Task ApplyDevelopmentAsync(HttpClient client) =>
        await ApplySourceAsync(client, "development");

    private static async Task ApplySourceAsync(HttpClient client, string source, string? release = null)
    {
        using var response = await client.PostAsync(
            "/api/source/apply",
            JsonContent(new { source, release }));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    private static Task<Baseline> GetBaselineAsync(PluginApplicationFactory factory) =>
        GetBaselineCoreAsync(factory.Services.GetRequiredService<IBaselineStore>());

    private static async Task<Baseline> GetBaselineCoreAsync(IBaselineStore baselineStore) =>
        await baselineStore.GetAsync(CancellationToken.None) ??
        throw new InvalidOperationException("Expected Apply to persist a Baseline.");

    private static Task<HttpResponseMessage> PostCommitAsync(
        HttpClient client,
        string message,
        params (string Repository, string Path)[] changes) =>
        client.PostAsync(
            "/api/runtime/commit",
            new StringContent(
                JsonSerializer.Serialize(new
                {
                    message,
                    changes = changes.Select(change => new { repository = change.Repository, path = change.Path })
                }),
                Encoding.UTF8,
                "application/json"));

    private static StringContent JsonContent<T>(T value) =>
        new(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json");

    private static async Task<string> GetOperationIdAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("operationId").GetString() ??
            throw new InvalidOperationException("Commit response did not include an operation ID.");
    }

    private static JsonElement GetStep(JsonElement report, string name) =>
        report.GetProperty("steps").EnumerateArray()
            .Single(step => step.GetProperty("name").GetString() == name);

    private static void AssertCommitMetadata(
        GitCommitInfo commit,
        string expectedAuthorName,
        string expectedAuthorEmail,
        string expectedMessage)
    {
        Assert.Equal(expectedAuthorName, commit.AuthorName);
        Assert.Equal(expectedAuthorEmail, commit.AuthorEmail);
        Assert.Equal(expectedAuthorName, commit.CommitterName);
        Assert.Equal(expectedAuthorEmail, commit.CommitterEmail);
        Assert.Equal(expectedMessage, commit.Message.TrimEnd('\r', '\n'));
    }

    private sealed class FailConfigsPushRemoteGit(IRemoteGit inner) : IRemoteGit
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
            repository is RepositoryId.Configs
                ? Task.FromResult(new RemoteGitPushResult(repository, RemoteGitPushStatus.Failed))
                : inner.PushAsync(repository, source, cancellationToken);
    }

    private sealed class FailAfterInitialBaselineSaveStore(IBaselineStore inner) : IBaselineStore
    {
        private int saveCount;

        public Task<Baseline?> GetAsync(CancellationToken cancellationToken) =>
            inner.GetAsync(cancellationToken);

        public Task SaveAsync(Baseline baseline, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref saveCount) > 1)
            {
                throw new IOException("Injected Runtime Commit Baseline persistence failure.");
            }

            return inner.SaveAsync(baseline, cancellationToken);
        }
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
