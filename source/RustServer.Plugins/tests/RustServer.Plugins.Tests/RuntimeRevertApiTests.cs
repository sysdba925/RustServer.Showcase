using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RustServer.Plugins.Features.PluginManagement.Baseline;
using RustServer.Plugins.Features.PluginManagement.Guards;
using RustServer.Plugins.Features.PluginManagement.Models;
using RustServer.Plugins.Features.PluginManagement.Operations;
using RustServer.Plugins.Features.PluginManagement.Runtime;
using Xunit;

namespace RustServer.Plugins.Tests;

public sealed class RuntimeRevertApiTests
{
    [Fact]
    public async Task FullRevertPreservesGitIgnoredRuntimeFiles()
    {
        using var plugins = await GitTestRepository.CreateAsync();
        using var configs = await GitTestRepository.CreateAsync();
        using var runtimeRoot = new TemporaryRuntimeRoot();
        await configs.AddRemoteCommitAsync(".gitignore", "data/*\n"u8.ToArray());
        await configs.AddRemoteCommitAsync("config/Managed.json", "original"u8.ToArray());
        var oxideRoot = CreateRuntimeRoot(runtimeRoot.ServerRootPath);

        using var factory = CreateFactory(plugins, configs, runtimeRoot.ServerRootPath);
        using var client = factory.CreateClient();
        await ApplyDevelopmentAsync(client);

        var ignored = Path.Combine(oxideRoot, "data", "PlayerState.json");
        await File.WriteAllTextAsync(ignored, "keep player data");
        var extra = Path.Combine(oxideRoot, "config", "Extra.json");
        await File.WriteAllTextAsync(extra, "remove");
        await File.WriteAllTextAsync(
            Path.Combine(oxideRoot, "config", "Managed.json"), "modified");

        using var response = await client.PostAsync("/api/runtime/revert", content: null);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal("keep player data", await File.ReadAllTextAsync(ignored));
        Assert.False(File.Exists(extra));
        Assert.Equal("original", await File.ReadAllTextAsync(
            Path.Combine(oxideRoot, "config", "Managed.json")));
    }

    [Fact]
    public async Task RevertRestoresTheExactBaselineWithoutFetchingOrChangingGitWorkspace()
    {
        using var plugins = await GitTestRepository.CreateAsync();
        using var configs = await GitTestRepository.CreateAsync();
        using var runtimeRoot = new TemporaryRuntimeRoot();
        var baselinePlugin = "baseline plugin"u8.ToArray();
        var baselineBinaryConfig = new byte[] { 0x00, 0xFF, 0xC3, 0x28 };
        await plugins.AddRemoteCommitAsync("Example.cs", baselinePlugin);
        await configs.AddRemoteCommitAsync("data/State.bin", baselineBinaryConfig);
        var oxidePath = CreateRuntimeRoot(runtimeRoot.ServerRootPath);

        using var factory = CreateFactory(plugins, configs, runtimeRoot.ServerRootPath);
        using var client = factory.CreateClient();
        await ApplyDevelopmentAsync(client);
        var baseline = await GetBaselineAsync(factory);
        var pluginsHeadBeforeRevert = await plugins.GetHeadAsync();
        var configsHeadBeforeRevert = await configs.GetHeadAsync();

        var pluginPath = Path.Combine(oxidePath, "plugins", "Example.cs");
        var binaryConfigPath = Path.Combine(oxidePath, "data", "State.bin");
        var extraPath = Path.Combine(oxidePath, "config", "Extra.json");
        await File.WriteAllBytesAsync(pluginPath, "runtime plugin change"u8.ToArray());
        File.Delete(binaryConfigPath);
        await File.WriteAllBytesAsync(extraPath, "{}"u8.ToArray());
        var newerRemotePluginHead = await plugins.AddRemoteCommitAsync("Example.cs", "newer remote plugin"u8.ToArray());

        using var revertResponse = await client.PostAsync("/api/runtime/revert", content: null);

        Assert.Equal(HttpStatusCode.Accepted, revertResponse.StatusCode);
        var operationId = await GetOperationIdAsync(revertResponse);
        Assert.Equal(baselinePlugin, await File.ReadAllBytesAsync(pluginPath));
        Assert.Equal(baselineBinaryConfig, await File.ReadAllBytesAsync(binaryConfigPath));
        Assert.False(File.Exists(extraPath));
        Assert.Equal(baseline, await GetBaselineAsync(factory));
        Assert.Equal(pluginsHeadBeforeRevert, await plugins.GetHeadAsync());
        Assert.Equal(configsHeadBeforeRevert, await configs.GetHeadAsync());
        Assert.Equal("develop", await plugins.GetCurrentBranchAsync());
        Assert.Equal("develop", await configs.GetCurrentBranchAsync());
        Assert.NotEqual(newerRemotePluginHead, await plugins.GetHeadAsync());

        using var diffResponse = await client.GetAsync("/api/runtime/diff");
        diffResponse.EnsureSuccessStatusCode();
        using var diff = JsonDocument.Parse(await diffResponse.Content.ReadAsStringAsync());
        Assert.Empty(diff.RootElement.GetProperty("changes").EnumerateArray());

        using var reportResponse = await client.GetAsync($"/api/operations/{operationId}");
        reportResponse.EnsureSuccessStatusCode();
        using var report = JsonDocument.Parse(await reportResponse.Content.ReadAsStringAsync());
        Assert.Equal("revertRuntime", report.RootElement.GetProperty("type").GetString());
        Assert.Equal("succeeded", report.RootElement.GetProperty("status").GetString());
        Assert.Equal("development", report.RootElement.GetProperty("source").GetProperty("type").GetString());
        Assert.Equal(1, report.RootElement.GetProperty("counts").GetProperty("added").GetInt32());
        Assert.Equal(1, report.RootElement.GetProperty("counts").GetProperty("modified").GetInt32());
        Assert.Equal(1, report.RootElement.GetProperty("counts").GetProperty("deleted").GetInt32());
        Assert.Equal(
            baseline.PluginsCommit.Value,
            report.RootElement.GetProperty("previousBaseline").GetProperty("pluginsCommit").GetString());
        Assert.Equal(
            baseline.PluginsCommit.Value,
            report.RootElement.GetProperty("finalBaseline").GetProperty("pluginsCommit").GetString());
        Assert.All(
            report.RootElement.GetProperty("steps").EnumerateArray(),
            step => Assert.Equal("completed", step.GetProperty("status").GetString()));
    }

    [Fact]
    public async Task RevertFailureRestoresThePreOperationRuntimeAndReleasesTheGuard()
    {
        using var plugins = await GitTestRepository.CreateAsync();
        using var configs = await GitTestRepository.CreateAsync();
        using var runtimeRoot = new TemporaryRuntimeRoot();
        var baselinePlugin = "baseline plugin"u8.ToArray();
        var baselineConfig = new byte[] { 0x00, 0xFF, 0x01 };
        await plugins.AddRemoteCommitAsync("Example.cs", baselinePlugin);
        await configs.AddRemoteCommitAsync("data/State.bin", baselineConfig);
        CreateRuntimeRoot(runtimeRoot.ServerRootPath);

        Baseline baseline;
        using (var setupFactory = CreateFactory(plugins, configs, runtimeRoot.ServerRootPath))
        using (var setupClient = setupFactory.CreateClient())
        {
            await ApplyDevelopmentAsync(setupClient);
            baseline = await GetBaselineAsync(setupFactory);
        }

        var mapping = new ManagedFileMapping();
        var pluginFile = mapping.FromRuntimePath("oxide/plugins/Example.cs")!;
        var configFile = mapping.FromRuntimePath("oxide/data/State.bin")!;
        var extraFile = mapping.FromRuntimePath("oxide/config/Extra.json")!;
        var originalPlugin = "runtime plugin change"u8.ToArray();
        var originalExtra = "{}"u8.ToArray();
        var failingRuntime = new FailingRuntimeFiles(
            [
                new RuntimeFile(pluginFile, originalPlugin),
                new RuntimeFile(extraFile, originalExtra)
            ],
            failOnWriteAttempt: 2);

        using var factory = CreateFactory(
            plugins,
            configs,
            runtimeRoot.ServerRootPath,
            services =>
            {
                services.RemoveAll<IRuntimeFiles>();
                services.AddSingleton<IRuntimeFiles>(failingRuntime);
            });
        using var client = factory.CreateClient();

        using var revertResponse = await client.PostAsync("/api/runtime/revert", content: null);

        Assert.Equal(HttpStatusCode.InternalServerError, revertResponse.StatusCode);
        using var error = JsonDocument.Parse(await revertResponse.Content.ReadAsStringAsync());
        Assert.Equal("RuntimeRevertFailed", error.RootElement.GetProperty("code").GetString());
        var operationId = error.RootElement.GetProperty("details").GetProperty("operationId").GetString();
        Assert.NotNull(operationId);
        Assert.Equal(originalPlugin, failingRuntime.ReadContent(pluginFile));
        Assert.Equal(originalExtra, failingRuntime.ReadContent(extraFile));
        Assert.False(failingRuntime.Contains(configFile));
        Assert.Equal(baseline, await GetBaselineAsync(factory));

        using var reportResponse = await client.GetAsync($"/api/operations/{operationId}");
        reportResponse.EnsureSuccessStatusCode();
        using var report = JsonDocument.Parse(await reportResponse.Content.ReadAsStringAsync());
        Assert.Equal("failed", report.RootElement.GetProperty("status").GetString());
        Assert.Equal("RuntimeRevertFailed", report.RootElement.GetProperty("failure").GetProperty("code").GetString());
        Assert.Equal("succeeded", report.RootElement.GetProperty("rollback").GetProperty("status").GetString());
        Assert.Equal(
            baseline.PluginsCommit.Value,
            report.RootElement.GetProperty("finalBaseline").GetProperty("pluginsCommit").GetString());

        var guard = factory.Services.GetRequiredService<IExclusiveMutationGuard>();
        var lease = guard.TryAcquire();
        Assert.NotNull(lease);
        await lease.DisposeAsync();
    }

    [Fact]
    public async Task RevertSelectedRestoresOnlySelectedAddedModifiedAndDeletedFiles()
    {
        using var plugins = await GitTestRepository.CreateAsync();
        using var configs = await GitTestRepository.CreateAsync();
        using var runtimeRoot = new TemporaryRuntimeRoot();
        await plugins.AddRemoteCommitAsync("Example.cs", "baseline selected"u8.ToArray());
        await plugins.AddRemoteCommitAsync("Unselected.cs", "baseline unselected"u8.ToArray());
        await configs.AddRemoteCommitAsync("data/State.bin", new byte[] { 0x00, 0x01, 0xFF });
        var oxidePath = CreateRuntimeRoot(runtimeRoot.ServerRootPath);

        using var factory = CreateFactory(plugins, configs, runtimeRoot.ServerRootPath);
        using var client = factory.CreateClient();
        await ApplyDevelopmentAsync(client);
        var baseline = await GetBaselineAsync(factory);

        var selectedPluginPath = Path.Combine(oxidePath, "plugins", "Example.cs");
        var unselectedPluginPath = Path.Combine(oxidePath, "plugins", "Unselected.cs");
        var deletedConfigPath = Path.Combine(oxidePath, "data", "State.bin");
        var addedConfigPath = Path.Combine(oxidePath, "config", "Extra.json");
        await File.WriteAllTextAsync(selectedPluginPath, "runtime selected change");
        await File.WriteAllTextAsync(unselectedPluginPath, "runtime unselected change");
        File.Delete(deletedConfigPath);
        await File.WriteAllTextAsync(addedConfigPath, "{}");

        using var response = await PostSelectedRevertAsync(
            client,
            ("plugins", "oxide/plugins/Example.cs"),
            ("configs", "oxide/data/State.bin"),
            ("configs", "oxide/config/Extra.json"));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var operationId = await GetOperationIdAsync(response);
        Assert.Equal("baseline selected", await File.ReadAllTextAsync(selectedPluginPath));
        Assert.Equal("runtime unselected change", await File.ReadAllTextAsync(unselectedPluginPath));
        Assert.Equal(new byte[] { 0x00, 0x01, 0xFF }, await File.ReadAllBytesAsync(deletedConfigPath));
        Assert.False(File.Exists(addedConfigPath));
        Assert.Equal(baseline, await GetBaselineAsync(factory));

        using var reportResponse = await client.GetAsync($"/api/operations/{operationId}");
        reportResponse.EnsureSuccessStatusCode();
        using var report = JsonDocument.Parse(await reportResponse.Content.ReadAsStringAsync());
        Assert.Equal(1, report.RootElement.GetProperty("counts").GetProperty("added").GetInt32());
        Assert.Equal(1, report.RootElement.GetProperty("counts").GetProperty("modified").GetInt32());
        Assert.Equal(1, report.RootElement.GetProperty("counts").GetProperty("deleted").GetInt32());
        Assert.Equal(3, report.RootElement.GetProperty("changes").GetArrayLength());
    }

    [Fact]
    public async Task RevertSelectedRejectsEmptyMalformedDuplicateAndMissingSelections()
    {
        using var plugins = await GitTestRepository.CreateAsync();
        using var configs = await GitTestRepository.CreateAsync();
        using var runtimeRoot = new TemporaryRuntimeRoot();
        await plugins.AddRemoteCommitAsync("Example.cs", "baseline"u8.ToArray());
        CreateRuntimeRoot(runtimeRoot.ServerRootPath);

        using var factory = CreateFactory(plugins, configs, runtimeRoot.ServerRootPath);
        using var client = factory.CreateClient();
        await ApplyDevelopmentAsync(client);

        var requests = new[]
        {
            "{\"changes\":[]}",
            "{\"changes\":null}",
            "{\"changes\":[{\"repository\":\"configs\",\"path\":\"oxide/plugins/Example.cs\"}]}",
            "{\"changes\":[{\"repository\":\"plugins\",\"path\":\"oxide/plugins/../Example.cs\"}]}",
            "{\"changes\":[{\"repository\":\"plugins\",\"path\":\"oxide/plugins/Example.cs\"},{\"repository\":\"plugins\",\"path\":\"oxide/plugins/Example.cs\"}]}",
            "{\"changes\":[{\"repository\":\"plugins\",\"path\":\"oxide/plugins/Example.cs\"}]}"
        };

        foreach (var request in requests)
        {
            using var response = await client.PostAsync(
                "/api/runtime/revert",
                new StringContent(request, Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            using var error = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal("InvalidRuntimeSelection", error.RootElement.GetProperty("code").GetString());
        }
    }

    [Fact]
    public async Task RevertSelectedFailureRollsBackOnlySelectedFiles()
    {
        using var plugins = await GitTestRepository.CreateAsync();
        using var configs = await GitTestRepository.CreateAsync();
        using var runtimeRoot = new TemporaryRuntimeRoot();
        await plugins.AddRemoteCommitAsync("Example.cs", "baseline plugin"u8.ToArray());
        await configs.AddRemoteCommitAsync("data/State.bin", new byte[] { 0x00, 0xFF, 0x01 });
        CreateRuntimeRoot(runtimeRoot.ServerRootPath);

        using (var setupFactory = CreateFactory(plugins, configs, runtimeRoot.ServerRootPath))
        using (var setupClient = setupFactory.CreateClient())
        {
            await ApplyDevelopmentAsync(setupClient);
        }

        var mapping = new ManagedFileMapping();
        var pluginFile = mapping.FromRuntimePath("oxide/plugins/Example.cs")!;
        var configFile = mapping.FromRuntimePath("oxide/data/State.bin")!;
        var extraFile = mapping.FromRuntimePath("oxide/config/Extra.json")!;
        var originalPlugin = "runtime plugin change"u8.ToArray();
        var originalExtra = "{}"u8.ToArray();
        var failingRuntime = new FailingRuntimeFiles(
            [
                new RuntimeFile(pluginFile, originalPlugin),
                new RuntimeFile(extraFile, originalExtra)
            ],
            failOnWriteAttempt: 2);

        using var factory = CreateFactory(
            plugins,
            configs,
            runtimeRoot.ServerRootPath,
            services =>
            {
                services.RemoveAll<IRuntimeFiles>();
                services.AddSingleton<IRuntimeFiles>(failingRuntime);
            });
        using var client = factory.CreateClient();

        using var response = await PostSelectedRevertAsync(
            client,
            ("plugins", "oxide/plugins/Example.cs"),
            ("configs", "oxide/data/State.bin"));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(originalPlugin, failingRuntime.ReadContent(pluginFile));
        Assert.Equal(originalExtra, failingRuntime.ReadContent(extraFile));
        Assert.False(failingRuntime.Contains(configFile));
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

    private static async Task<string> GetOperationIdAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("operationId").GetString() ??
            throw new InvalidOperationException("Runtime Revert response did not include an operation ID.");
    }

    private static Task<HttpResponseMessage> PostSelectedRevertAsync(
        HttpClient client,
        params (string Repository, string Path)[] changes) =>
        client.PostAsync(
            "/api/runtime/revert",
            new StringContent(
                JsonSerializer.Serialize(new
                {
                    changes = changes.Select(change => new
                    {
                        repository = change.Repository,
                        path = change.Path
                    })
                }),
                Encoding.UTF8,
                "application/json"));

    private sealed class FailingRuntimeFiles(
        IEnumerable<RuntimeFile> initialFiles,
        int failOnWriteAttempt) : IRuntimeFiles
    {
        private readonly Dictionary<string, RuntimeFile> files = initialFiles.ToDictionary(
            file => file.File.RuntimeRelativePath,
            StringComparer.Ordinal);
        private int writeAttempts;

        public Task<RuntimeAvailability> GetAvailabilityAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new RuntimeAvailability(RuntimeAvailabilityStatus.Available));

        public Task<IReadOnlyList<ManagedFile>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ManagedFile>>(
                files.Values.Select(file => file.File).OrderBy(file => file.RuntimeRelativePath, StringComparer.Ordinal).ToArray());

        public Task<byte[]> ReadAsync(ManagedFile file, CancellationToken cancellationToken) =>
            Task.FromResult(files[file.RuntimeRelativePath].Content.ToArray());

        public Task WriteAsync(ManagedFile file, ReadOnlyMemory<byte> content, CancellationToken cancellationToken)
        {
            writeAttempts++;
            if (writeAttempts == failOnWriteAttempt)
            {
                throw new IOException("Injected Runtime write failure.");
            }

            files[file.RuntimeRelativePath] = new RuntimeFile(file, content.ToArray());
            return Task.CompletedTask;
        }

        public Task DeleteAsync(ManagedFile file, CancellationToken cancellationToken)
        {
            files.Remove(file.RuntimeRelativePath);
            return Task.CompletedTask;
        }

        public bool Contains(ManagedFile file) => files.ContainsKey(file.RuntimeRelativePath);

        public byte[] ReadContent(ManagedFile file) => files[file.RuntimeRelativePath].Content.ToArray();
    }

    private sealed record RuntimeFile(ManagedFile File, byte[] Content);

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
