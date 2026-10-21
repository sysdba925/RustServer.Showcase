using System.Text.Json;
using RustServer.Control.Application.Processes;
using RustServer.Control.Application.Servers;
using RustServer.Control.Infrastructure.Servers;

namespace RustServer.Control.Tests;

public sealed class RustServerUpdaterTests
{
    [Fact]
    public async Task UpdateAsync_InvokesSteamCmdComposeUtilityWithConfiguredPaths()
    {
        var executor = new FakeProcessExecutor(new ProcessExecutionResult(0, "updated", string.Empty));
        var updater = CreateUpdater(executor);

        await updater.UpdateAsync();

        var request = Assert.IsType<ProcessExecutionRequest>(executor.Request);
        Assert.Equal("docker", request.FileName);
        Assert.Equal(
            ["compose", "-f", "C:\\steamcmd\\compose.yml", "run", "--rm", "steamcmd"],
            request.Arguments);
    }

    [Fact]
    public async Task UpdateAsync_WhenProcessExitsWithZero_ReturnsCompactSucceededResult()
    {
        var updater = CreateUpdater(new FakeProcessExecutor(new ProcessExecutionResult(0, "updated", string.Empty)));

        var result = await updater.UpdateAsync();

        Assert.True(result.Succeeded);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public async Task UpdateAsync_WhenProcessExitsWithNonZeroCode_ReturnsCompactFailedResult()
    {
        var updater = CreateUpdater(new FakeProcessExecutor(new ProcessExecutionResult(23, string.Empty, "update failed")));

        var result = await updater.UpdateAsync();

        Assert.False(result.Succeeded);
        Assert.Equal(23, result.ExitCode);
    }

    [Fact]
    public async Task UpdateAsync_PassesCancellationTokenToProcessExecutor()
    {
        var executor = new FakeProcessExecutor(new ProcessExecutionResult(0, string.Empty, string.Empty));
        var updater = CreateUpdater(executor);
        using var cancellationTokenSource = new CancellationTokenSource();

        await updater.UpdateAsync(cancellationTokenSource.Token);

        Assert.Equal(cancellationTokenSource.Token, executor.CancellationToken);
    }

    [Fact]
    public async Task UpdateResponse_DoesNotSerializeLargeProcessOutput()
    {
        var updater = CreateUpdater(new FakeProcessExecutor(new ProcessExecutionResult(
            0,
            new string('o', 100_000),
            new string('e', 100_000))));
        var response = await updater.UpdateAsync();

        var json = JsonSerializer.Serialize(
            response,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var document = JsonDocument.Parse(json);

        Assert.Equal(2, document.RootElement.EnumerateObject().Count());
        Assert.True(document.RootElement.TryGetProperty("succeeded", out _));
        Assert.True(document.RootElement.TryGetProperty("exitCode", out _));
        Assert.True(json.Length < 100);
    }

    private static RustServerUpdater CreateUpdater(FakeProcessExecutor executor) =>
        new(
            executor,
            new RustServerUpdateOptions
            {
                SteamCmdComposePath = "C:\\steamcmd\\compose.yml"
            });

    private sealed class FakeProcessExecutor(ProcessExecutionResult result) : IProcessExecutor
    {
        public ProcessExecutionRequest? Request { get; private set; }

        public CancellationToken CancellationToken { get; private set; }

        public Task<ProcessExecutionResult> ExecuteAsync(
            ProcessExecutionRequest request,
            CancellationToken cancellationToken = default)
        {
            Request = request;
            CancellationToken = cancellationToken;
            return Task.FromResult(result);
        }
    }
}
