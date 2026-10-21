using RustServer.Control.Application.Processes;
using RustServer.Control.Infrastructure.Servers;
using RustServer.Control.Infrastructure.StartupTracking;

namespace RustServer.Control.Tests;

public sealed class DockerComposeStartupLogSourceTests
{
    private const string StartedAt = "2026-09-29T17:00:00.123456789Z";

    [Fact]
    public async Task ReadCurrentRunAsync_ReadsOnlyCurrentRunAndMergesTimestampedStreams()
    {
        var executor = new RecordingProcessExecutor(
            Success("short-container-id\n"),
            Success($"full-container-id|{StartedAt}\n"),
            Success(
                "2026-09-29T17:00:00.000000000Z Initialize engine version: 6000.3.15x1-11 (d4a0e1288074)\n" +
                "2026-09-29T17:00:02.000000000Z Bootstrap Startup\n",
                "2026-09-29T17:00:01.000000000Z Begin MonoManager ReloadAssembly\n"),
            Success("full-container-id\n"),
            Success($"full-container-id|{StartedAt}\n"));
        var source = new DockerComposeStartupLogSource(executor, CreateOptions(), CreateRconPort());

        var run = await source.ReadCurrentRunAsync();

        var actualRun = Assert.IsType<RustServer.Control.Application.StartupTracking.StartupRunLog>(run);
        Assert.Equal("full-container-id", actualRun.ContainerId);
        Assert.Equal(StartedAt, actualRun.StartedAt);
        Assert.Equal(
            [
                "Initialize engine version: 6000.3.15x1-11 (d4a0e1288074)",
                "Begin MonoManager ReloadAssembly",
                "Bootstrap Startup"
            ],
            actualRun.Lines);
        Assert.Equal(
            ["compose", "-f", "/opt/rust-server/compose.yml", "ps", "-q", "rust-server"],
            executor.Requests[0].Arguments);
        Assert.Equal("28016", executor.Requests[0].EnvironmentVariables!["RUST_SERVER_RCON_PORT"]);
        Assert.Equal(
            ["inspect", "--format", "{{.Id}}|{{.State.StartedAt}}", "short-container-id"],
            executor.Requests[1].Arguments);
        Assert.Equal(
            ["logs", "--timestamps", "--since", StartedAt, "full-container-id"],
            executor.Requests[2].Arguments);
    }

    [Fact]
    public async Task ReadCurrentRunAsync_PreservesDeterministicStdOutFirstOrderForEqualTimestamps()
    {
        var executor = new RecordingProcessExecutor(
            Success("container-id\n"),
            Success($"container-id|{StartedAt}\n"),
            Success(
                "2026-09-29T17:00:01.000000000Z stdout line\n",
                "2026-09-29T17:00:01.000000000Z stderr line\n"),
            Success("container-id\n"),
            Success($"container-id|{StartedAt}\n"));
        var source = new DockerComposeStartupLogSource(executor, CreateOptions(), CreateRconPort());

        var run = await source.ReadCurrentRunAsync();

        Assert.Equal(["stdout line", "stderr line"], run?.Lines);
    }

    [Fact]
    public async Task ReadCurrentRunAsync_WhenNoContainerIsRunning_ReturnsNullWithoutInspectingOrReadingLogs()
    {
        var executor = new RecordingProcessExecutor(Success(string.Empty));
        var source = new DockerComposeStartupLogSource(executor, CreateOptions(), CreateRconPort());

        var run = await source.ReadCurrentRunAsync();

        Assert.Null(run);
        Assert.Single(executor.Requests);
    }

    [Fact]
    public async Task ReadCurrentRunAsync_WhenDockerCommandFails_ThrowsTrackingExceptionWithoutLeakingOutput()
    {
        var executor = new RecordingProcessExecutor(new ProcessExecutionResult(
            ExitCode: 1,
            StdOut: "sensitive docker output",
            StdErr: "sensitive docker error"));
        var source = new DockerComposeStartupLogSource(executor, CreateOptions(), CreateRconPort());

        var exception = await Assert.ThrowsAsync<StartupTrackingException>(() => source.ReadCurrentRunAsync());

        Assert.Equal("container lookup", exception.Operation);
        Assert.DoesNotContain("sensitive", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReadCurrentRunAsync_WhenContainerChangesDuringCollection_RetriesAndReturnsCoherentNewRun()
    {
        const string firstStartedAt = "2026-09-29T17:00:00.000000000Z";
        const string secondStartedAt = "2026-09-29T18:00:00.000000000Z";
        var executor = new RecordingProcessExecutor(
            Success("container-a\n"),
            Success($"container-a|{firstStartedAt}\n"),
            Success("2026-09-29T17:00:01.000000000Z SteamServer Connected\n"),
            Success("container-b\n"),
            Success($"container-b|{secondStartedAt}\n"),
            Success("container-b\n"),
            Success($"container-b|{secondStartedAt}\n"),
            Success("2026-09-29T18:00:01.000000000Z Initialize engine version: 6000.3.15x1-11 (d4a0e1288074)\n"),
            Success("container-b\n"),
            Success($"container-b|{secondStartedAt}\n"));
        var source = new DockerComposeStartupLogSource(executor, CreateOptions(), CreateRconPort());

        var run = await source.ReadCurrentRunAsync();

        var actualRun = Assert.IsType<RustServer.Control.Application.StartupTracking.StartupRunLog>(run);
        Assert.Equal("container-b", actualRun.ContainerId);
        Assert.Equal(secondStartedAt, actualRun.StartedAt);
        Assert.Equal(["Initialize engine version: 6000.3.15x1-11 (d4a0e1288074)"], actualRun.Lines);
        Assert.Equal(10, executor.Requests.Count);
    }

    [Fact]
    public async Task ReadCurrentRunAsync_PassesCallerCancellationTokenToDockerCommands()
    {
        var executor = new RecordingProcessExecutor(
            Success(string.Empty));
        var source = new DockerComposeStartupLogSource(executor, CreateOptions(), CreateRconPort());
        using var cancellationTokenSource = new CancellationTokenSource();

        await source.ReadCurrentRunAsync(cancellationTokenSource.Token);

        Assert.All(
            executor.CancellationTokens,
            cancellationToken => Assert.Equal(cancellationTokenSource.Token, cancellationToken));
    }

    [Fact]
    public async Task ReadCurrentRunAsync_WhenConfigurationIsMissing_ThrowsExistingConfigurationException()
    {
        var source = new DockerComposeStartupLogSource(
            new RecordingProcessExecutor(),
            new RustServerControlOptions(),
            CreateRconPort());

        await Assert.ThrowsAsync<RustServerControlConfigurationException>(() => source.ReadCurrentRunAsync());
    }

    private static RustServerControlOptions CreateOptions() => new()
    {
        RustServerComposePath = "/opt/rust-server/compose.yml"
    };

    private static RustServerRconPort CreateRconPort() => new(28016);

    private static ProcessExecutionResult Success(string standardOutput, string standardError = "") =>
        new(0, standardOutput, standardError);

    private sealed class RecordingProcessExecutor(params ProcessExecutionResult[] results) : IProcessExecutor
    {
        private readonly Queue<ProcessExecutionResult> _results = new(results);

        public List<ProcessExecutionRequest> Requests { get; } = [];

        public List<CancellationToken> CancellationTokens { get; } = [];

        public Task<ProcessExecutionResult> ExecuteAsync(
            ProcessExecutionRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            CancellationTokens.Add(cancellationToken);

            return Task.FromResult(_results.Count > 0
                ? _results.Dequeue()
                : Success(string.Empty));
        }
    }
}
