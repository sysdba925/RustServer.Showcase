using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RustServer.Plugins.Features.PluginManagement.Baseline;
using RustServer.Plugins.Features.PluginManagement.Commands.ApplySource;
using RustServer.Plugins.Features.PluginManagement.Git;
using RustServer.Plugins.Features.PluginManagement.Guards;
using RustServer.Plugins.Features.PluginManagement.Models;
using RustServer.Plugins.Features.PluginManagement.Models.Git;
using RustServer.Plugins.Features.PluginManagement.Models.Operations;
using RustServer.Plugins.Features.PluginManagement.Operations;
using RustServer.Plugins.Features.PluginManagement.Runtime;
using Xunit;

namespace RustServer.Plugins.Tests;

public sealed class ApplySourceApiTests
{
    [Fact]
    public async Task ApplyPreservesGitIgnoredRuntimeDataWhileRemovingUnmanagedNonignoredFiles()
    {
        using var plugins = await GitTestRepository.CreateAsync();
        using var configs = await GitTestRepository.CreateAsync();
        using var runtimeRoot = new TemporaryRuntimeRoot();
        await configs.AddRemoteCommitAsync(".gitignore", "data/*\n"u8.ToArray());
        await configs.AddRemoteCommitAsync("config/Managed.json", "managed"u8.ToArray());

        var oxideRoot = CreateRuntimeRoot(runtimeRoot.ServerRootPath);
        var ignored = Path.Combine(oxideRoot, "data", "PlayerState.json");
        var unmanaged = Path.Combine(oxideRoot, "config", "Obsolete.json");
        await File.WriteAllTextAsync(ignored, "keep player data");
        await File.WriteAllTextAsync(unmanaged, "remove");

        using var factory = CreateFactory(plugins, configs, runtimeRoot.ServerRootPath);
        using var client = factory.CreateClient();
        using var response = await client.PostAsync(
            "/api/source/apply",
            new StringContent("{\"source\":\"development\"}", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal("keep player data", await File.ReadAllTextAsync(ignored));
        Assert.False(File.Exists(unmanaged));
        Assert.Equal("managed", await File.ReadAllTextAsync(
            Path.Combine(oxideRoot, "config", "Managed.json")));
    }

    [Fact]
    public async Task ApplyFreshFetchesAndMakesRuntimeExactlyMatchTheTargetSource()
    {
        using var plugins = await GitTestRepository.CreateAsync();
        using var configs = await GitTestRepository.CreateAsync();
        using var runtimeRoot = new TemporaryRuntimeRoot();
        var targetPlugin = "target plugin"u8.ToArray();
        var targetConfig = new byte[] { 0x00, 0xFF, 0x01 };
        var targetPluginsHead = await plugins.AddRemoteCommitAsync("Example.cs", targetPlugin);
        var targetConfigsHead = await configs.AddRemoteCommitAsync("data/Target.bin", targetConfig);
        var oxidePath = CreateRuntimeRoot(runtimeRoot.ServerRootPath);
        await File.WriteAllTextAsync(Path.Combine(oxidePath, "plugins", "Example.cs"), "runtime plugin");
        await File.WriteAllTextAsync(Path.Combine(oxidePath, "config", "Obsolete.json"), "{}");

        using var factory = CreateFactory(plugins, configs, runtimeRoot.ServerRootPath);
        using var client = factory.CreateClient();
        using var applyResponse = await client.PostAsync(
            "/api/source/apply",
            new StringContent("{\"source\":\"development\"}", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.Accepted, applyResponse.StatusCode);
        var operationId = await GetOperationIdAsync(applyResponse);
        Assert.Equal(targetPluginsHead, await plugins.GetHeadAsync());
        Assert.Equal(targetConfigsHead, await configs.GetHeadAsync());
        Assert.Equal(targetPlugin, await File.ReadAllBytesAsync(Path.Combine(oxidePath, "plugins", "Example.cs")));
        Assert.Equal(targetConfig, await File.ReadAllBytesAsync(Path.Combine(oxidePath, "data", "Target.bin")));
        Assert.False(File.Exists(Path.Combine(oxidePath, "config", "Obsolete.json")));

        var baseline = await factory.Services.GetRequiredService<IBaselineStore>().GetAsync(CancellationToken.None);
        Assert.NotNull(baseline);
        Assert.Equal(Source.Development, baseline.Source);
        Assert.Equal(targetPluginsHead, baseline.PluginsCommit.Value);
        Assert.Equal(targetConfigsHead, baseline.ConfigsCommit.Value);

        using var reportResponse = await client.GetAsync($"/api/operations/{operationId}");
        reportResponse.EnsureSuccessStatusCode();
        using var report = JsonDocument.Parse(await reportResponse.Content.ReadAsStringAsync());
        Assert.Equal("applySource", report.RootElement.GetProperty("type").GetString());
        Assert.Equal("succeeded", report.RootElement.GetProperty("status").GetString());
        Assert.Equal(1, report.RootElement.GetProperty("counts").GetProperty("added").GetInt32());
        Assert.Equal(1, report.RootElement.GetProperty("counts").GetProperty("modified").GetInt32());
        Assert.Equal(1, report.RootElement.GetProperty("counts").GetProperty("deleted").GetInt32());
        Assert.Equal(targetPluginsHead, GetGitState(report.RootElement, "plugins").GetProperty("head").GetString());
        Assert.Equal(targetConfigsHead, GetGitState(report.RootElement, "configs").GetProperty("head").GetString());
        Assert.Equal(targetPluginsHead, report.RootElement.GetProperty("finalBaseline").GetProperty("pluginsCommit").GetString());
        Assert.All(
            report.RootElement.GetProperty("steps").EnumerateArray(),
            step => Assert.Equal("completed", step.GetProperty("status").GetString()));
    }

    [Fact]
    public async Task BaselinePersistenceFailureRestoresRuntimeAndWorkspaceAndReleasesGuard()
    {
        using var plugins = await GitTestRepository.CreateAsync();
        using var configs = await GitTestRepository.CreateAsync();
        using var runtimeRoot = new TemporaryRuntimeRoot();
        var previousPluginsHead = await plugins.GetHeadAsync();
        var previousConfigsHead = await configs.GetHeadAsync();
        await plugins.AddRemoteCommitAsync("Example.cs", "target plugin"u8.ToArray());
        await configs.AddRemoteCommitAsync("data/Target.bin", [0x00, 0xFF, 0x01]);
        var oxidePath = CreateRuntimeRoot(runtimeRoot.ServerRootPath);
        var originalRuntime = "original runtime"u8.ToArray();
        await File.WriteAllBytesAsync(Path.Combine(oxidePath, "plugins", "Original.cs"), originalRuntime);
        var previousBaseline = new Baseline(
            PluginEnvironment.Staging,
            Source.Development,
            new GitCommitSha(previousPluginsHead),
            new GitCommitSha(previousConfigsHead),
            DateTimeOffset.UtcNow);
        var failingBaselineStore = new FailingBaselineStore(previousBaseline);

        using var factory = CreateFactory(
            plugins,
            configs,
            runtimeRoot.ServerRootPath,
            services =>
            {
                services.RemoveAll<IBaselineStore>();
                services.AddSingleton<IBaselineStore>(failingBaselineStore);
            });
        using var client = factory.CreateClient();
        using var applyResponse = await client.PostAsync(
            "/api/source/apply",
            new StringContent("{\"source\":\"development\"}", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.InternalServerError, applyResponse.StatusCode);
        using var error = JsonDocument.Parse(await applyResponse.Content.ReadAsStringAsync());
        Assert.Equal("BaselinePersistFailed", error.RootElement.GetProperty("code").GetString());
        var operationId = error.RootElement.GetProperty("details").GetProperty("operationId").GetString();
        Assert.NotNull(operationId);
        Assert.Equal(previousPluginsHead, await plugins.GetHeadAsync());
        Assert.Equal(previousConfigsHead, await configs.GetHeadAsync());
        Assert.Equal("develop", await plugins.GetCurrentBranchAsync());
        Assert.Equal("develop", await configs.GetCurrentBranchAsync());
        Assert.Equal(originalRuntime, await File.ReadAllBytesAsync(Path.Combine(oxidePath, "plugins", "Original.cs")));
        Assert.False(File.Exists(Path.Combine(oxidePath, "plugins", "Example.cs")));
        Assert.False(File.Exists(Path.Combine(oxidePath, "data", "Target.bin")));
        Assert.Equal(previousBaseline, await failingBaselineStore.GetAsync(CancellationToken.None));

        using var reportResponse = await client.GetAsync($"/api/operations/{operationId}");
        reportResponse.EnsureSuccessStatusCode();
        using var report = JsonDocument.Parse(await reportResponse.Content.ReadAsStringAsync());
        Assert.Equal("failed", report.RootElement.GetProperty("status").GetString());
        Assert.Equal("BaselinePersistFailed", report.RootElement.GetProperty("failure").GetProperty("code").GetString());
        Assert.Equal("succeeded", report.RootElement.GetProperty("rollback").GetProperty("status").GetString());
        Assert.Equal(previousPluginsHead, GetGitState(report.RootElement, "plugins").GetProperty("head").GetString());
        Assert.Equal(previousPluginsHead, report.RootElement.GetProperty("finalBaseline").GetProperty("pluginsCommit").GetString());

        var guard = factory.Services.GetRequiredService<IExclusiveMutationGuard>();
        var lease = guard.TryAcquire();
        Assert.NotNull(lease);
        await lease.DisposeAsync();
    }

    [Fact]
    public async Task CancellationAfterWorkspacePreparationRollsBackAndStopsGitChildrenBeforeGuardRelease()
    {
        using var plugins = await GitTestRepository.CreateAsync();
        using var configs = await GitTestRepository.CreateAsync();
        using var runtimeRoot = new TemporaryRuntimeRoot();
        var previousPluginsHead = await plugins.GetHeadAsync();
        var previousConfigsHead = await configs.GetHeadAsync();
        await plugins.AddRemoteCommitAsync("Example.cs", "target plugin"u8.ToArray());
        await configs.AddRemoteCommitAsync("data/Target.bin", [0x00, 0xFF, 0x01]);
        using var cancellationSource = new CancellationTokenSource();
        var mapping = new ManagedFileMapping();
        var originalFile = mapping.FromRuntimePath("oxide/plugins/Original.cs")!;
        var originalContent = "original runtime"u8.ToArray();
        var runtimeFiles = new CancelingRuntimeFiles(originalFile, originalContent, cancellationSource);

        using var factory = CreateFactory(
            plugins,
            configs,
            runtimeRoot.ServerRootPath,
            services =>
            {
                services.RemoveAll<IRuntimeFiles>();
                services.AddSingleton<IRuntimeFiles>(runtimeFiles);
            });
        var handler = factory.Services.GetRequiredService<ApplySourceHandler>();

        var result = await handler.HandleAsync(Source.Development, cancellationSource.Token);

        Assert.Equal(ApplySourceFailureReason.OperationCanceled, result.Failure?.Reason);
        Assert.NotNull(result.OperationId);
        Assert.Equal(previousPluginsHead, await plugins.GetHeadAsync());
        Assert.Equal(previousConfigsHead, await configs.GetHeadAsync());
        Assert.Equal("develop", await plugins.GetCurrentBranchAsync());
        Assert.Equal("develop", await configs.GetCurrentBranchAsync());
        Assert.Equal(originalContent, runtimeFiles.ReadContent(originalFile));
        Assert.False(runtimeFiles.Contains("oxide/data/Target.bin"));

        var reports = factory.Services.GetRequiredService<IOperationReports>();
        var report = reports.Get(result.OperationId!);
        Assert.NotNull(report);
        Assert.Equal(OperationStatus.Failed, report.Status);
        Assert.Equal("OperationCanceled", report.Failure?.Code);
        Assert.Equal(RollbackStatus.Succeeded, report.Rollback?.Status);
        Assert.Equal(previousPluginsHead, GetGitState(report, RepositoryId.Plugins).Head?.Value);
        Assert.Equal(previousConfigsHead, GetGitState(report, RepositoryId.Configs).Head?.Value);

        using var client = factory.CreateClient();
        using var reportResponse = await client.GetAsync($"/api/operations/{result.OperationId.Value}");
        reportResponse.EnsureSuccessStatusCode();
        using var reportDocument = JsonDocument.Parse(await reportResponse.Content.ReadAsStringAsync());
        Assert.Equal("failed", reportDocument.RootElement.GetProperty("status").GetString());
        Assert.Equal("OperationCanceled", reportDocument.RootElement.GetProperty("failure").GetProperty("code").GetString());

        var guard = factory.Services.GetRequiredService<IExclusiveMutationGuard>();
        var lease = guard.TryAcquire();
        Assert.NotNull(lease);
        try
        {
            var gitProcess = factory.Services.GetRequiredService<GitProcess>();
            var childStartedPath = Path.Combine(plugins.WorkspacePath, ".git-child-started");
            var lateMarkerPath = Path.Combine(plugins.WorkspacePath, ".git-child-late");
            using var gitCancellationSource = new CancellationTokenSource();
            // Git executes shell aliases through its cross-platform POSIX shell. The alias child
            // writes the startup marker itself, so cancellation cannot race ahead of child startup.
            var gitTask = gitProcess.ExecuteAsync(
                plugins.WorkspacePath,
                [
                    "-c",
                    "alias.delayed=!: > .git-child-started; sleep 2; : > .git-child-late",
                    "delayed"
                ],
                gitCancellationSource.Token);

            await WaitForFileAsync(childStartedPath, gitTask, TimeSpan.FromSeconds(5));
            gitCancellationSource.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => gitTask);
            await Task.Delay(TimeSpan.FromSeconds(3));

            Assert.False(File.Exists(lateMarkerPath));
        }
        finally
        {
            await lease.DisposeAsync();
        }

        Assert.Equal(previousPluginsHead, await plugins.GetHeadAsync());
        Assert.Equal(previousConfigsHead, await configs.GetHeadAsync());
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

    private static async Task<string> GetOperationIdAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("operationId").GetString() ??
            throw new InvalidOperationException("Apply response did not include an operation ID.");
    }

    private static JsonElement GetGitState(JsonElement report, string repository) =>
        report.GetProperty("finalGitState")
            .EnumerateArray()
            .Single(state => state.GetProperty("repository").GetString() == repository);

    private static OperationGitState GetGitState(OperationReport report, RepositoryId repository) =>
        Assert.Single(report.FinalGitStates, state => state.Repository == repository);

    private static async Task WaitForFileAsync(string path, Task processTask, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (!File.Exists(path))
        {
            if (processTask.IsCompleted)
            {
                await processTask;
                throw new InvalidOperationException("Git completed before its child process signaled startup.");
            }

            if (DateTimeOffset.UtcNow >= deadline)
            {
                throw new TimeoutException($"Git child process did not create '{path}' within {timeout}.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(10));
        }
    }

    private sealed class FailingBaselineStore(Baseline baseline) : IBaselineStore
    {
        public Task<Baseline?> GetAsync(CancellationToken cancellationToken) =>
            Task.FromResult<Baseline?>(baseline);

        public Task SaveAsync(Baseline baseline, CancellationToken cancellationToken) =>
            throw new IOException("Injected baseline persistence failure.");
    }

    private sealed class CancelingRuntimeFiles(
        ManagedFile originalFile,
        byte[] originalContent,
        CancellationTokenSource cancellationSource) : IRuntimeFiles
    {
        private readonly Dictionary<string, RuntimeFile> files = new(StringComparer.Ordinal)
        {
            [originalFile.RuntimeRelativePath] = new(originalFile, originalContent.ToArray())
        };
        private bool cancellationTriggered;

        public Task<RuntimeAvailability> GetAvailabilityAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new RuntimeAvailability(RuntimeAvailabilityStatus.Available));

        public Task<IReadOnlyList<ManagedFile>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ManagedFile>>(files.Values.Select(file => file.File).ToArray());

        public Task<byte[]> ReadAsync(ManagedFile file, CancellationToken cancellationToken) =>
            Task.FromResult(files[file.RuntimeRelativePath].Content.ToArray());

        public Task WriteAsync(ManagedFile file, ReadOnlyMemory<byte> content, CancellationToken cancellationToken)
        {
            files[file.RuntimeRelativePath] = new RuntimeFile(file, content.ToArray());
            if (!cancellationTriggered)
            {
                cancellationTriggered = true;
                cancellationSource.Cancel();
                cancellationToken.ThrowIfCancellationRequested();
            }

            return Task.CompletedTask;
        }

        public Task DeleteAsync(ManagedFile file, CancellationToken cancellationToken)
        {
            files.Remove(file.RuntimeRelativePath);
            return Task.CompletedTask;
        }

        public bool Contains(string runtimeRelativePath) => files.ContainsKey(runtimeRelativePath);

        public byte[] ReadContent(ManagedFile file) => files[file.RuntimeRelativePath].Content.ToArray();

        private sealed record RuntimeFile(ManagedFile File, byte[] Content);
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
