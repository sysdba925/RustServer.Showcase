using System.Net;
using System.Text.Json;
using Xunit;

namespace RustServer.Plugins.Tests;

public sealed class OperationReportsApiTests
{
    [Fact]
    public async Task SuccessfulFetchStoresCompletedReportWithFinalGitState()
    {
        using var plugins = await GitTestRepository.CreateAsync();
        using var configs = await GitTestRepository.CreateAsync();
        using var factory = CreateFactory(plugins, configs);
        var pluginsHead = await plugins.AddRemoteCommitAsync("plugins-update.txt");
        var configsHead = await configs.AddRemoteCommitAsync("configs-update.txt");
        using var client = factory.CreateClient();

        using var fetchResponse = await client.PostAsync("/api/git/fetch", content: null);
        Assert.Equal(HttpStatusCode.Accepted, fetchResponse.StatusCode);
        var operationId = await GetOperationIdAsync(fetchResponse);

        using var reportResponse = await client.GetAsync($"/api/operations/{operationId}");
        reportResponse.EnsureSuccessStatusCode();

        using var report = JsonDocument.Parse(await reportResponse.Content.ReadAsStringAsync());
        Assert.Equal("fetchGit", report.RootElement.GetProperty("type").GetString());
        Assert.Equal("succeeded", report.RootElement.GetProperty("status").GetString());
        Assert.Equal("completed", report.RootElement.GetProperty("currentPhase").GetString());
        Assert.Equal("notAttempted", report.RootElement.GetProperty("rollback").GetProperty("status").GetString());
        Assert.All(
            report.RootElement.GetProperty("steps").EnumerateArray(),
            step => Assert.Equal("completed", step.GetProperty("status").GetString()));
        var finalGitState = report.RootElement.GetProperty("finalGitState").EnumerateArray().ToArray();
        Assert.Equal(2, finalGitState.Length);
        Assert.NotEqual(pluginsHead, GetRepositoryState(finalGitState, "plugins").GetProperty("head").GetString());
        Assert.NotEqual(configsHead, GetRepositoryState(finalGitState, "configs").GetProperty("head").GetString());
    }

    [Fact]
    public async Task PartialFetchFailureStoresCompletedAndFailedStepsWithFinalState()
    {
        using var plugins = await GitTestRepository.CreateAsync();
        using var configs = await GitTestRepository.CreateAsync();
        using var factory = CreateFactory(plugins, configs);
        await configs.RemoveOriginAsync();
        using var client = factory.CreateClient();

        using var fetchResponse = await client.PostAsync("/api/git/fetch", content: null);
        Assert.Equal(HttpStatusCode.Conflict, fetchResponse.StatusCode);
        var operationId = await GetOperationIdAsync(fetchResponse);

        using var reportResponse = await client.GetAsync($"/api/operations/{operationId}");
        reportResponse.EnsureSuccessStatusCode();

        using var report = JsonDocument.Parse(await reportResponse.Content.ReadAsStringAsync());
        Assert.Equal("failed", report.RootElement.GetProperty("status").GetString());
        Assert.Equal("fetchConfigs", report.RootElement.GetProperty("currentPhase").GetString());
        var failure = report.RootElement.GetProperty("failure");
        Assert.Equal("configs", failure.GetProperty("repository").GetString());
        Assert.Equal("GitRemoteMissing", failure.GetProperty("code").GetString());
        var steps = report.RootElement.GetProperty("steps").EnumerateArray().ToArray();
        Assert.Equal("completed", GetStep(steps, "fetchPlugins").GetProperty("status").GetString());
        Assert.Equal("failed", GetStep(steps, "fetchConfigs").GetProperty("status").GetString());
        Assert.Equal("completed", GetStep(steps, "captureFinalState").GetProperty("status").GetString());
        Assert.Equal(2, report.RootElement.GetProperty("finalGitState").GetArrayLength());
    }

    [Fact]
    public async Task HistoryEndpointReturnsCompactSummariesAndValidatesPaginationQuery()
    {
        using var plugins = await GitTestRepository.CreateAsync();
        using var configs = await GitTestRepository.CreateAsync();
        using var factory = CreateFactory(plugins, configs);
        using var client = factory.CreateClient();

        using var fetchResponse = await client.PostAsync("/api/git/fetch", content: null);
        Assert.Equal(HttpStatusCode.Accepted, fetchResponse.StatusCode);

        using var historyResponse = await client.GetAsync("/api/operations?limit=1");
        historyResponse.EnsureSuccessStatusCode();
        using var history = JsonDocument.Parse(await historyResponse.Content.ReadAsStringAsync());
        var item = Assert.Single(history.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal("fetchGit", item.GetProperty("type").GetString());
        Assert.Equal("succeeded", item.GetProperty("status").GetString());
        Assert.True(item.TryGetProperty("startedAt", out _));
        Assert.True(item.TryGetProperty("completedAt", out _));
        Assert.True(item.TryGetProperty("changedFiles", out _));

        using var maximumLimit = await client.GetAsync("/api/operations?limit=200");
        maximumLimit.EnsureSuccessStatusCode();

        using var invalidLimit = await client.GetAsync("/api/operations?limit=0");
        Assert.Equal(HttpStatusCode.BadRequest, invalidLimit.StatusCode);
        using var tooLargeLimit = await client.GetAsync("/api/operations?limit=201");
        Assert.Equal(HttpStatusCode.BadRequest, tooLargeLimit.StatusCode);
        using var invalidCursor = await client.GetAsync("/api/operations?cursor=not-a-cursor");
        Assert.Equal(HttpStatusCode.BadRequest, invalidCursor.StatusCode);
    }

    [Fact]
    public async Task DetailedReportIsAvailableAfterApplicationRestart()
    {
        using var plugins = await GitTestRepository.CreateAsync();
        using var configs = await GitTestRepository.CreateAsync();
        var configuration = new Dictionary<string, string?>
        {
            ["PluginManagement:StateRootPath"] = Path.Combine(plugins.RootPath, "state"),
            ["PluginManagement:Repositories:Plugins:WorkspacePath"] = plugins.WorkspacePath,
            ["PluginManagement:Repositories:Configs:WorkspacePath"] = configs.WorkspacePath
        };
        string operationId;

        using (var firstFactory = new PluginApplicationFactory(configuration))
        using (var firstClient = firstFactory.CreateClient())
        using (var fetchResponse = await firstClient.PostAsync("/api/git/fetch", content: null))
        {
            Assert.Equal(HttpStatusCode.Accepted, fetchResponse.StatusCode);
            operationId = await GetOperationIdAsync(fetchResponse);
        }

        using var restartedFactory = new PluginApplicationFactory(configuration);
        using var restartedClient = restartedFactory.CreateClient();
        using var reportResponse = await restartedClient.GetAsync($"/api/operations/{operationId}");
        reportResponse.EnsureSuccessStatusCode();
        using var report = JsonDocument.Parse(await reportResponse.Content.ReadAsStringAsync());
        Assert.Equal(operationId, report.RootElement.GetProperty("operationId").GetString());
        Assert.Equal("succeeded", report.RootElement.GetProperty("status").GetString());
    }

    private static async Task<string> GetOperationIdAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var details = document.RootElement.TryGetProperty("details", out var errorDetails)
            ? errorDetails
            : document.RootElement;

        return details.GetProperty("operationId").GetString() ??
            throw new InvalidOperationException("Fetch response did not include an operation ID.");
    }

    private static JsonElement GetStep(IEnumerable<JsonElement> steps, string name) =>
        steps.Single(step => string.Equals(step.GetProperty("name").GetString(), name, StringComparison.Ordinal));

    private static JsonElement GetRepositoryState(IEnumerable<JsonElement> states, string repository) =>
        states.Single(state => string.Equals(state.GetProperty("repository").GetString(), repository, StringComparison.Ordinal));

    private static PluginApplicationFactory CreateFactory(
        GitTestRepository plugins,
        GitTestRepository configs) =>
        new(new Dictionary<string, string?>
        {
            ["PluginManagement:StateRootPath"] = Path.Combine(plugins.RootPath, "state"),
            ["PluginManagement:Repositories:Plugins:WorkspacePath"] = plugins.WorkspacePath,
            ["PluginManagement:Repositories:Configs:WorkspacePath"] = configs.WorkspacePath
        });
}
