using System.Net;
using System.Text;
using System.Text.Json;
using Xunit;

namespace RustServer.Plugins.Tests;

public sealed class PluginManagementAcceptanceTests
{
    [Fact]
    public async Task FirstInitialization_FetchesPreviewsAppliesAndCreatesBaseline()
    {
        using var fixture = await AcceptanceFixture.CreateAsync();
        var pluginContent = "development plugin"u8.ToArray();
        var configContent = "{\"environment\":\"development\"}"u8.ToArray();
        await fixture.Plugins.AddRemoteCommitAsync("Example.cs", pluginContent);
        await fixture.Configs.AddRemoteCommitAsync("config/Example.json", configContent);

        using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();

        var initialEnvironment = await GetJsonAsync(client, "/api/environment");
        Assert.Equal("staging", initialEnvironment.GetProperty("environment").GetString());
        Assert.Equal("uninitialized", initialEnvironment.GetProperty("systemState").GetString());

        await FetchAsync(client);

        var preview = await PostSourcePreviewAsync(client, "development");
        Assert.Equal("development", preview.GetProperty("targetSource").GetProperty("type").GetString());
        Assert.Equal(JsonValueKind.Null, preview.GetProperty("baseline").ValueKind);

        var applyReport = await ApplySourceAsync(client, "development");
        Assert.Equal("applySource", applyReport.GetProperty("type").GetString());
        Assert.Equal("succeeded", applyReport.GetProperty("status").GetString());

        var baseline = await GetJsonAsync(client, "/api/baseline");
        Assert.Equal("ready", baseline.GetProperty("status").GetString());
        Assert.Equal("development", baseline.GetProperty("baseline").GetProperty("source").GetString());
        Assert.Equal(pluginContent, await File.ReadAllBytesAsync(fixture.RuntimePath("oxide", "plugins", "Example.cs")));
        Assert.Equal(configContent, await File.ReadAllBytesAsync(fixture.RuntimePath("oxide", "config", "Example.json")));

        var readyEnvironment = await GetJsonAsync(client, "/api/environment");
        Assert.Equal("ready", readyEnvironment.GetProperty("systemState").GetString());
    }

    [Fact]
    public async Task RuntimeDevelopmentWorkflow_CommitsPushesAndAdvancesBaselineWithoutApply()
    {
        using var fixture = await AcceptanceFixture.CreateAsync();
        var initialContent = "initial plugin"u8.ToArray();
        await fixture.Plugins.AddRemoteCommitAsync("Example.cs", initialContent);

        using var factory = fixture.CreateFactory(serverIsRunning: true);
        using var client = factory.CreateClient();
        await ApplySourceAsync(client, "development");
        var baselineBeforeCommit = await GetJsonAsync(client, "/api/baseline");
        var baselinePluginsCommit = baselineBeforeCommit.GetProperty("baseline").GetProperty("pluginsCommit").GetString();

        var runtimeContent = "committed runtime plugin"u8.ToArray();
        await File.WriteAllBytesAsync(fixture.RuntimePath("oxide", "plugins", "Example.cs"), runtimeContent);

        var runtimeDiff = await GetJsonAsync(client, "/api/runtime/diff");
        var change = Assert.Single(runtimeDiff.GetProperty("changes").EnumerateArray());
        Assert.Equal("modified", change.GetProperty("change").GetString());

        var commitReport = await CommitRuntimeAsync(
            client,
            "Persist Runtime plugin",
            ("plugins", "oxide/plugins/Example.cs"));
        Assert.Equal("commitRuntime", commitReport.GetProperty("type").GetString());
        Assert.Equal("succeeded", commitReport.GetProperty("status").GetString());
        Assert.Equal(runtimeContent, await fixture.Plugins.GetRemoteFileAsync("develop", "Example.cs"));

        var baselineAfterCommit = await GetJsonAsync(client, "/api/baseline");
        Assert.Equal(
            await fixture.Plugins.GetRemoteHeadAsync("develop"),
            baselineAfterCommit.GetProperty("baseline").GetProperty("pluginsCommit").GetString());
        Assert.NotEqual(
            baselinePluginsCommit,
            baselineAfterCommit.GetProperty("baseline").GetProperty("pluginsCommit").GetString());
        var systemState = await GetJsonAsync(client, "/api/environment");
        Assert.Equal("ready", systemState.GetProperty("systemState").GetString());
        using (var releasesResponse = await client.GetAsync("/api/releases"))
        {
            releasesResponse.EnsureSuccessStatusCode();
        }

        var clearedDiff = await GetJsonAsync(client, "/api/runtime/diff");
        Assert.Empty(clearedDiff.GetProperty("changes").EnumerateArray());
    }

    [Fact]
    public async Task ProductionRelease_AllowsLiveApplyAndRevertButStillBlocksProductionCommit()
    {
        using var fixture = await AcceptanceFixture.CreateAsync();
        await fixture.Plugins.AddRemoteCommitAsync("Example.cs", "development"u8.ToArray());
        await fixture.Configs.AddRemoteCommitAsync("config/Example.json", "{\"version\":\"development\"}"u8.ToArray());
        await fixture.Plugins.AddRemoteReleaseAsync("1.0");
        await fixture.Configs.AddRemoteReleaseAsync("1.0");

        using (var stoppedFactory = fixture.CreateFactory(serverIsRunning: true))
        using (var stoppedClient = stoppedFactory.CreateClient())
        {
            await ApplySourceAsync(stoppedClient, "development");
            await SetEnvironmentAsync(stoppedClient, "production");

            using (var statusResponse = await stoppedClient.GetAsync("/api/git/status"))
            using (var releasesResponse = await stoppedClient.GetAsync("/api/releases"))
            {
                statusResponse.EnsureSuccessStatusCode();
                releasesResponse.EnsureSuccessStatusCode();
            }

            var preview = await PostSourcePreviewAsync(stoppedClient, "release", "1.0");
            Assert.Equal("release", preview.GetProperty("targetSource").GetProperty("type").GetString());
            await ApplySourceAsync(stoppedClient, "release", "1.0");
            var baselineBeforeRevert = await GetJsonAsync(stoppedClient, "/api/baseline");

            await File.WriteAllTextAsync(fixture.RuntimePath("oxide", "plugins", "Example.cs"), "runtime change");
            await File.WriteAllTextAsync(fixture.RuntimePath("oxide", "config", "Added.json"), "{}");
            var changedDiff = await GetJsonAsync(stoppedClient, "/api/runtime/diff");
            Assert.NotEmpty(changedDiff.GetProperty("changes").EnumerateArray());

            using (var commitResponse = await stoppedClient.PostAsync(
                       "/api/runtime/commit",
                       JsonContent(new
                       {
                           message = "Production commit must be blocked",
                           changes = new[] { new { repository = "plugins", path = "oxide/plugins/Example.cs" } }
                       })))
            {
                Assert.Equal(HttpStatusCode.Conflict, commitResponse.StatusCode);
                Assert.Equal("CommitNotAllowedInProduction", (await ReadJsonAsync(commitResponse)).GetProperty("code").GetString());
            }

            using (var developmentApply = await stoppedClient.PostAsync(
                       "/api/source/apply",
                       JsonContent(new { source = "development" })))
            {
                Assert.Equal(HttpStatusCode.Conflict, developmentApply.StatusCode);
                Assert.Equal("SourceNotAllowed", (await ReadJsonAsync(developmentApply)).GetProperty("code").GetString());
            }

            var revertReport = await RevertRuntimeAsync(stoppedClient);
            Assert.Equal("revertRuntime", revertReport.GetProperty("type").GetString());
            Assert.Equal("succeeded", revertReport.GetProperty("status").GetString());
            var clearedDiff = await GetJsonAsync(stoppedClient, "/api/runtime/diff");
            Assert.Empty(clearedDiff.GetProperty("changes").EnumerateArray());
            var baselineAfterRevert = await GetJsonAsync(stoppedClient, "/api/baseline");
            Assert.Equal(
                baselineBeforeRevert.GetProperty("baseline").GetRawText(),
                baselineAfterRevert.GetProperty("baseline").GetRawText());
        }

        using var runningFactory = fixture.CreateFactory(serverIsRunning: true);
        using var runningClient = runningFactory.CreateClient();
        using (var diffResponse = await runningClient.GetAsync("/api/runtime/diff"))
        {
            diffResponse.EnsureSuccessStatusCode();
        }

        using var applyResponse = await runningClient.PostAsync(
            "/api/source/apply",
            JsonContent(new { source = "release", release = "1.0" }));
        Assert.Equal(HttpStatusCode.Accepted, applyResponse.StatusCode);
        var report = await GetOperationReportAsync(runningClient, await GetOperationIdAsync(applyResponse));
        Assert.Equal("succeeded", report.GetProperty("status").GetString());
    }

    private static async Task FetchAsync(HttpClient client)
    {
        using var response = await client.PostAsync("/api/git/fetch", content: null);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    private static async Task<JsonElement> PostSourcePreviewAsync(
        HttpClient client,
        string source,
        string? release = null)
    {
        using var response = await client.PostAsync("/api/source/preview", JsonContent(new { source, release }));
        response.EnsureSuccessStatusCode();
        return await ReadJsonAsync(response);
    }

    private static async Task<JsonElement> ApplySourceAsync(HttpClient client, string source, string? release = null)
    {
        using var response = await client.PostAsync("/api/source/apply", JsonContent(new { source, release }));
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        return await GetOperationReportAsync(client, await GetOperationIdAsync(response));
    }

    private static async Task<JsonElement> CommitRuntimeAsync(
        HttpClient client,
        string message,
        params (string Repository, string Path)[] changes)
    {
        using var response = await client.PostAsync(
            "/api/runtime/commit",
            JsonContent(new
            {
                message,
                changes = changes.Select(change => new { repository = change.Repository, path = change.Path })
            }));
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        return await GetOperationReportAsync(client, await GetOperationIdAsync(response));
    }

    private static async Task<JsonElement> RevertRuntimeAsync(HttpClient client)
    {
        using var response = await client.PostAsync("/api/runtime/revert", content: null);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        return await GetOperationReportAsync(client, await GetOperationIdAsync(response));
    }

    private static async Task SetEnvironmentAsync(HttpClient client, string environment)
    {
        using var response = await client.PutAsync("/api/environment", JsonContent(new { environment }));
        response.EnsureSuccessStatusCode();
    }

    private static async Task<JsonElement> GetJsonAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path);
        response.EnsureSuccessStatusCode();
        return await ReadJsonAsync(response);
    }

    private static async Task<JsonElement> GetOperationReportAsync(HttpClient client, string operationId)
    {
        var report = await GetJsonAsync(client, $"/api/operations/{operationId}");
        Assert.Equal("succeeded", report.GetProperty("status").GetString());
        return report;
    }

    private static async Task<string> GetOperationIdAsync(HttpResponseMessage response)
    {
        var responseBody = await ReadJsonAsync(response);
        return responseBody.GetProperty("operationId").GetString() ??
            throw new InvalidOperationException("Mutation response did not contain an operation ID.");
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    private static StringContent JsonContent<T>(T value) =>
        new(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json");

    private sealed class AcceptanceFixture : IDisposable
    {
        private AcceptanceFixture(GitTestRepository plugins, GitTestRepository configs, string runtimeRootPath)
        {
            Plugins = plugins;
            Configs = configs;
            RuntimeRootPath = runtimeRootPath;
        }

        public GitTestRepository Plugins { get; }

        public GitTestRepository Configs { get; }

        private string RuntimeRootPath { get; }

        private string ServerRootPath => Path.Combine(RuntimeRootPath, "server");

        public static async Task<AcceptanceFixture> CreateAsync()
        {
            var plugins = await GitTestRepository.CreateAsync();
            GitTestRepository? configs = null;
            string? runtimeRootPath = null;
            try
            {
                configs = await GitTestRepository.CreateAsync();
                runtimeRootPath = Path.Combine(
                    Path.GetTempPath(),
                    "RustServer.Plugins.Tests",
                    Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(Path.Combine(runtimeRootPath, "server", "oxide", "plugins"));
                Directory.CreateDirectory(Path.Combine(runtimeRootPath, "server", "oxide", "config"));
                Directory.CreateDirectory(Path.Combine(runtimeRootPath, "server", "oxide", "data"));
                return new AcceptanceFixture(plugins, configs, runtimeRootPath);
            }
            catch
            {
                plugins.Dispose();
                configs?.Dispose();
                if (runtimeRootPath is not null && Directory.Exists(runtimeRootPath))
                {
                    Directory.Delete(runtimeRootPath, recursive: true);
                }

                throw;
            }
        }

        public PluginApplicationFactory CreateFactory(bool serverIsRunning = false) =>
            new(
                new Dictionary<string, string?>
                {
                    ["PluginManagement:StateRootPath"] = Path.Combine(Plugins.RootPath, "state"),
                    ["PluginManagement:Repositories:Plugins:WorkspacePath"] = Plugins.WorkspacePath,
                    ["PluginManagement:Repositories:Configs:WorkspacePath"] = Configs.WorkspacePath,
                    ["PluginManagement:Runtime:ServerRootPath"] = ServerRootPath
                },
                serverIsRunning: serverIsRunning);

        public string RuntimePath(params string[] segments) =>
            Path.Combine([ServerRootPath, .. segments]);

        public void Dispose()
        {
            Plugins.Dispose();
            Configs.Dispose();

            if (Directory.Exists(RuntimeRootPath))
            {
                Directory.Delete(RuntimeRootPath, recursive: true);
            }
        }
    }
}
