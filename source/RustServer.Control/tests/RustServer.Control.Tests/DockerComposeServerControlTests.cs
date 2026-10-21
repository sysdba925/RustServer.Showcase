using RustServer.Control.Application.Processes;
using RustServer.Control.Application.NetworkAccess;
using RustServer.Control.Application.Servers;
using RustServer.Control.Domain.NetworkAccess;
using RustServer.Control.Domain.Operations;
using RustServer.Control.Domain.Servers;
using RustServer.Control.Infrastructure.Operations;
using RustServer.Control.Infrastructure.Servers;

namespace RustServer.Control.Tests;

public sealed class DockerComposeServerControlTests
{
    [Fact]
    public async Task StartAsync_InvokesComposeUpForStoppedRustServer()
    {
        var executor = new RecordingProcessExecutor(
            new ProcessExecutionResult(0, string.Empty, string.Empty),
            new ProcessExecutionResult(0, string.Empty, string.Empty),
            new ProcessExecutionResult(0, "container-id\n", string.Empty));
        var commandClient = new RecordingServerCommandClient();
        var control = CreateControl(executor, commandClient);

        var status = await control.StartAsync();

        Assert.True(status.IsRunning);
        Assert.False(status.IsReady);
        Assert.Empty(commandClient.Commands);
        Assert.Equal(
            ["compose", "-f", "/opt/rust-server/compose.yml", "up", "-d", "rust-server"],
            executor.Requests[1].Arguments);
        Assert.Equal(
            ["compose", "-f", "/opt/rust-server/compose.yml", "ps", "-q", "rust-server"],
            executor.Requests[2].Arguments);
    }

    [Fact]
    public async Task StartAsync_KeepsPublicPortsClosedBeforeAndAfterContainerStarts()
    {
        var executor = new RecordingProcessExecutor(
            new ProcessExecutionResult(0, string.Empty, string.Empty),
            new ProcessExecutionResult(0, string.Empty, string.Empty),
            new ProcessExecutionResult(0, "container-id\n", string.Empty));
        var applier = new RecordingNetworkAccessRuleApplier();
        var settings = new NetworkAccessSettings(PublicEnabled: false, MaintenancePort: 50_016);
        var control = CreateControl(executor, networkAccessSettings: settings, networkAccessRuleApplier: applier);

        await control.StartAsync();

        Assert.Equal([settings, settings], applier.Applied);
    }

    [Fact]
    public async Task RestartAsync_KeepsPublicPortsClosedBeforeAndAfterRecreate()
    {
        var executor = new RecordingProcessExecutor(
            new ProcessExecutionResult(0, string.Empty, string.Empty),
            new ProcessExecutionResult(0, string.Empty, string.Empty),
            new ProcessExecutionResult(0, "new-container-id\n", string.Empty));
        var applier = new RecordingNetworkAccessRuleApplier();
        var settings = new NetworkAccessSettings(PublicEnabled: false, MaintenancePort: 50_016);
        var control = CreateControl(executor, networkAccessSettings: settings, networkAccessRuleApplier: applier);

        await control.RestartAsync();

        Assert.Equal([settings, settings], applier.Applied);
    }

    [Fact]
    public async Task StartAsync_WithDesiredPublicOn_NeverOpensBeforeRustReady()
    {
        var executor = new RecordingProcessExecutor(
            new ProcessExecutionResult(0, string.Empty, string.Empty),
            new ProcessExecutionResult(0, string.Empty, string.Empty),
            new ProcessExecutionResult(0, "container-id\\n", string.Empty));
        var applier = new RecordingNetworkAccessRuleApplier();
        var control = CreateControl(
            executor,
            networkAccessSettings: new NetworkAccessSettings(true, 50_016),
            networkAccessRuleApplier: applier);

        await control.StartAsync();

        Assert.Equal([
            new NetworkAccessSettings(false, 50_016),
            new NetworkAccessSettings(false, 50_016)
        ], applier.Applied);
    }

    [Fact]
    public async Task StartAsync_WhenNetworkAccessCannotBeApplied_PropagatesTheHelperFailure()
    {
        var executor = new RecordingProcessExecutor(
            new ProcessExecutionResult(0, string.Empty, string.Empty),
            new ProcessExecutionResult(0, string.Empty, string.Empty),
            new ProcessExecutionResult(0, "container-id\n", string.Empty));
        var failure = new NetworkAccessRuleApplicationException(
            NetworkAccessRuleFailureKind.HelperUnavailable,
            "The privileged NetworkAccess helper is unavailable.");
        var control = CreateControl(
            executor,
            networkAccessRuleApplier: new RecordingNetworkAccessRuleApplier { Failure = failure });

        var exception = await Assert.ThrowsAsync<NetworkAccessRuleApplicationException>(() => control.StartAsync());

        Assert.Same(failure, exception);
    }

    [Fact]
    public async Task StartAsync_WhenServerIsAlreadyRunning_SkipsComposeUpAndRcon()
    {
        var executor = new RecordingProcessExecutor(
            new ProcessExecutionResult(0, "container-id\n", string.Empty));
        var commandClient = new RecordingServerCommandClient();
        var control = CreateControl(executor, commandClient);

        var status = await control.StartAsync();

        Assert.True(status.IsRunning);
        Assert.False(status.IsReady);
        Assert.Empty(commandClient.Commands);
        Assert.DoesNotContain(executor.Requests, request => request.Arguments.Contains("up"));
    }

    [Fact]
    public async Task StartAsync_CanBeCalledRepeatedly()
    {
        var executor = new RecordingProcessExecutor(
            new ProcessExecutionResult(0, string.Empty, string.Empty),
            new ProcessExecutionResult(0, string.Empty, string.Empty),
            new ProcessExecutionResult(0, "container-id\n", string.Empty),
            new ProcessExecutionResult(0, "container-id\n", string.Empty));
        var control = CreateControl(executor);

        await control.StartAsync();
        await control.StartAsync();

        Assert.Equal(4, executor.Requests.Count);
        Assert.Equal(
            ["compose", "-f", "/opt/rust-server/compose.yml", "up", "-d", "rust-server"],
            executor.Requests[1].Arguments);
        Assert.DoesNotContain(
            executor.Requests.Skip(2),
            request => request.Arguments.Contains("up"));
    }

    [Fact]
    public async Task StartAsync_UsesDesiredConfigurationAndRecordsItAsEffectiveAfterContainerStarts()
    {
        var desired = new ServerConfiguration(
            "Configured Rust",
            "Configured by Control",
            42,
            20,
            900,
            123456,
            4200);
        var configurationStore = new InMemoryServerConfigurationStore(desired);
        var executor = new RecordingProcessExecutor(
            new ProcessExecutionResult(0, string.Empty, string.Empty),
            new ProcessExecutionResult(0, string.Empty, string.Empty),
            new ProcessExecutionResult(0, "container-id\n", string.Empty));
        var control = CreateControl(executor, configurationStore: configurationStore);

        await control.StartAsync();

        var request = executor.Requests[1];
        Assert.NotNull(request.EnvironmentVariables);
        Assert.Equal("Configured Rust", request.EnvironmentVariables!["RUST_SERVER_HOSTNAME"]);
        Assert.Equal("Configured by Control", request.EnvironmentVariables["RUST_SERVER_DESCRIPTION"]);
        Assert.Equal("42", request.EnvironmentVariables["RUST_SERVER_MAX_PLAYERS"]);
        Assert.Equal("20", request.EnvironmentVariables["RUST_SERVER_TICK_RATE"]);
        Assert.Equal("900", request.EnvironmentVariables["RUST_SERVER_SAVE_INTERVAL"]);
        Assert.Equal("123456", request.EnvironmentVariables["RUST_SERVER_SEED"]);
        Assert.Equal("4200", request.EnvironmentVariables["RUST_SERVER_WORLD_SIZE"]);
        Assert.Equal("28016", request.EnvironmentVariables["RUST_SERVER_RCON_PORT"]);
        Assert.Equal(desired, configurationStore.Get().Effective);
    }

    [Fact]
    public async Task StopAsync_WhenServerIsStopped_SkipsRconAndComposeStop()
    {
        var executor = new RecordingProcessExecutor(
            new ProcessExecutionResult(0, string.Empty, string.Empty));
        var commandClient = new RecordingServerCommandClient();
        var control = CreateControl(executor, commandClient);

        var status = await control.StopAsync();

        Assert.False(status.IsRunning);
        Assert.False(status.IsReady);
        Assert.Empty(commandClient.Commands);
        Assert.Equal(
            ["compose", "-f", "/opt/rust-server/compose.yml", "ps", "-q", "rust-server"],
            Assert.Single(executor.Requests).Arguments);
    }

    [Fact]
    public async Task StopAsync_WhenRconSucceedsAndContainerStops_DoesNotUseComposeFallback()
    {
        var executor = new RecordingProcessExecutor(
            new ProcessExecutionResult(0, "container-id\n", string.Empty),
            new ProcessExecutionResult(0, string.Empty, string.Empty));
        var commandClient = new RecordingServerCommandClient();
        var control = CreateControl(executor, commandClient);

        var status = await control.StopAsync();

        Assert.False(status.IsRunning);
        Assert.False(status.IsReady);
        Assert.Equal(["sv quit"], commandClient.Commands);
        Assert.DoesNotContain(executor.Requests, request => request.Arguments.Contains("stop"));
        Assert.Equal(
            ["compose", "-f", "/opt/rust-server/compose.yml", "ps", "-q", "rust-server"],
            executor.Requests[^1].Arguments);
    }

    [Fact]
    public async Task StopAsync_WhenSvQuitClosesWebSocketAndContainerStops_TreatsShutdownAsSuccessful()
    {
        var executor = new RecordingProcessExecutor(
            new ProcessExecutionResult(0, "container-id\n", string.Empty),
            new ProcessExecutionResult(0, string.Empty, string.Empty));
        var commandClient = new RecordingServerCommandClient(
            new RconClientException("connection closed", new InvalidOperationException()));
        var control = CreateControl(executor, commandClient);

        var status = await control.StopAsync();

        Assert.False(status.IsRunning);
        Assert.False(status.IsReady);
        Assert.Equal(["sv quit"], commandClient.Commands);
        Assert.DoesNotContain(executor.Requests, request => request.Arguments.Contains("stop"));
    }

    [Fact]
    public async Task StopAsync_WhenRconFailsAndContainerRemainsRunning_UsesComposeFallback()
    {
        var executor = new StopFallbackProcessExecutor();
        var commandClient = new RecordingServerCommandClient(
            new RconClientException("connection failed", new InvalidOperationException()));
        var control = CreateControl(
            executor,
            commandClient,
            CreateOptions(TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(1)));

        var status = await control.StopAsync();

        Assert.False(status.IsRunning);
        Assert.False(status.IsReady);
        Assert.Equal(["sv quit"], commandClient.Commands);
        Assert.Contains(executor.Requests, request => request.Arguments.Contains("stop"));
        Assert.Equal(
            ["compose", "-f", "/opt/rust-server/compose.yml", "ps", "-q", "rust-server"],
            executor.Requests[^1].Arguments);
    }

    [Fact]
    public async Task StopAsync_WhenGracefulShutdownTimesOut_UsesComposeFallback()
    {
        var executor = new StopFallbackProcessExecutor();
        var commandClient = new RecordingServerCommandClient();
        var control = CreateControl(
            executor,
            commandClient,
            CreateOptions(TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(1)));

        var status = await control.StopAsync();

        Assert.False(status.IsRunning);
        Assert.False(status.IsReady);
        Assert.Equal(["sv quit"], commandClient.Commands);
        Assert.Contains(executor.Requests, request => request.Arguments.Contains("stop"));
    }

    [Fact]
    public async Task StopAsync_WhenComposeFallbackFails_PreservesLifecycleException()
    {
        var executor = new StopFallbackProcessExecutor(stopExitCode: 1);
        var control = CreateControl(
            executor,
            new RecordingServerCommandClient(),
            CreateOptions(TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(1)));

        var exception = await Assert.ThrowsAsync<RustServerControlException>(() => control.StopAsync());

        Assert.Equal("stop", exception.Operation);
        Assert.Equal(1, exception.ExitCode);
    }

    [Fact]
    public async Task RestartAsync_WhenServerIsRunning_UsesGracefulShutdownBeforeRecreatingService()
    {
        var executor = new RecordingProcessExecutor(
            new ProcessExecutionResult(0, "container-id\n", string.Empty),
            new ProcessExecutionResult(0, string.Empty, string.Empty),
            new ProcessExecutionResult(0, string.Empty, string.Empty),
            new ProcessExecutionResult(0, "new-container-id\n", string.Empty));
        var commandClient = new RecordingServerCommandClient();
        var control = CreateControl(executor, commandClient);

        var status = await control.RestartAsync();

        Assert.True(status.IsRunning);
        Assert.False(status.IsReady);
        Assert.Equal(["sv quit"], commandClient.Commands);
        Assert.Equal(
            ["compose", "-f", "/opt/rust-server/compose.yml", "up", "-d", "--force-recreate", "rust-server"],
            executor.Requests[2].Arguments);
        Assert.Equal(
            ["compose", "-f", "/opt/rust-server/compose.yml", "ps", "-q", "rust-server"],
            executor.Requests[3].Arguments);
    }

    [Fact]
    public async Task RestartAsync_WhenServerIsStopped_SkipsShutdownAndRecreatesService()
    {
        var executor = new RecordingProcessExecutor(
            new ProcessExecutionResult(0, string.Empty, string.Empty),
            new ProcessExecutionResult(0, string.Empty, string.Empty),
            new ProcessExecutionResult(0, "container-id\n", string.Empty));
        var commandClient = new RecordingServerCommandClient();
        var control = CreateControl(executor, commandClient);

        var status = await control.RestartAsync();

        Assert.True(status.IsRunning);
        Assert.False(status.IsReady);
        Assert.Empty(commandClient.Commands);
        Assert.Equal(
            ["compose", "-f", "/opt/rust-server/compose.yml", "up", "-d", "--force-recreate", "rust-server"],
            executor.Requests[1].Arguments);
        Assert.Equal(
            ["compose", "-f", "/opt/rust-server/compose.yml", "ps", "-q", "rust-server"],
            executor.Requests[2].Arguments);
    }

    [Fact]
    public async Task GetStatusAsync_WhenContainerIsStopped_ReturnsNotReadyWithoutCallingRcon()
    {
        var executor = new RecordingProcessExecutor(new ProcessExecutionResult(0, string.Empty, string.Empty));
        var commandClient = new RecordingServerCommandClient();
        var control = CreateControl(executor, commandClient);

        var status = await control.GetStatusAsync();

        Assert.False(status.IsRunning);
        Assert.False(status.IsReady);
        Assert.Empty(commandClient.Commands);
        Assert.Equal(
            ["compose", "-f", "/opt/rust-server/compose.yml", "ps", "-q", "rust-server"],
            Assert.Single(executor.Requests).Arguments);
    }

    [Fact]
    public async Task GetStatusAsync_WhenContainerIsRunningAndRconSucceeds_ReturnsReady()
    {
        var executor = new RecordingProcessExecutor(
            new ProcessExecutionResult(0, "container-id\n", string.Empty));
        var commandClient = new RecordingServerCommandClient();
        var control = CreateControl(executor, commandClient);

        var status = await control.GetStatusAsync();

        Assert.True(status.IsRunning);
        Assert.True(status.IsReady);
        Assert.Equal(["status"], commandClient.Commands);
    }

    [Theory]
    [InlineData("connection refused")]
    [InlineData("command response timed out")]
    [InlineData("WebSocket connection closed")]
    public async Task GetStatusAsync_WhenRconReadinessProbeFails_ReturnsRunningButNotReady(string errorMessage)
    {
        var executor = new RecordingProcessExecutor(
            new ProcessExecutionResult(0, "container-id\n", string.Empty));
        var commandClient = new RecordingServerCommandClient(
            new RconClientException(errorMessage, new InvalidOperationException()));
        var control = CreateControl(executor, commandClient);

        var status = await control.GetStatusAsync();

        Assert.True(status.IsRunning);
        Assert.False(status.IsReady);
        Assert.Equal(["status"], commandClient.Commands);
    }

    [Fact]
    public async Task GetStatusAsync_WhenReadinessTimeoutExpires_ReturnsRunningButNotReady()
    {
        var executor = new RecordingProcessExecutor(
            new ProcessExecutionResult(0, "container-id\n", string.Empty));
        var commandClient = new WaitForCancellationServerCommandClient();
        var control = CreateControl(
            executor,
            commandClient,
            CreateOptions(readinessTimeout: TimeSpan.FromMilliseconds(10)));

        var status = await control.GetStatusAsync();

        Assert.True(status.IsRunning);
        Assert.False(status.IsReady);
        Assert.Equal(["status"], commandClient.Commands);
        Assert.True(commandClient.CancellationRequested);
    }

    [Fact]
    public async Task GetStatusAsync_WhenCallerCancels_PropagatesCancellation()
    {
        var executor = new RecordingProcessExecutor(
            new ProcessExecutionResult(0, "container-id\n", string.Empty));
        var commandClient = new WaitForCancellationServerCommandClient();
        var control = CreateControl(executor, commandClient);
        using var cancellationTokenSource = new CancellationTokenSource();

        var statusTask = control.GetStatusAsync(cancellationTokenSource.Token);
        await commandClient.CommandStarted.Task;
        cancellationTokenSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => statusTask);
    }

    [Fact]
    public async Task StartAsync_PassesCancellationTokenToProcessExecutor()
    {
        var executor = new RecordingProcessExecutor();
        var control = CreateControl(executor);
        using var cancellationTokenSource = new CancellationTokenSource();

        await control.StartAsync(cancellationTokenSource.Token);

        Assert.All(
            executor.CancellationTokens,
            cancellationToken => Assert.Equal(cancellationTokenSource.Token, cancellationToken));
    }

    [Fact]
    public async Task LifecycleOperations_AreSerialized()
    {
        var executor = new BlockingProcessExecutor();
        var control = CreateControl(executor);

        var startTask = control.StartAsync();
        await executor.FirstExecutionStarted.Task;

        var stopTask = control.StopAsync();
        Assert.Equal(1, executor.ExecutionCount);

        executor.AllowFirstExecutionToComplete.SetResult();
        await Task.WhenAll(startTask, stopTask);

        Assert.Equal(4, executor.ExecutionCount);
    }

    [Fact]
    public async Task StartAsync_WhenRustUpdateIsPending_RejectsMutationBeforeAcquiringTheLock()
    {
        var executor = new RecordingProcessExecutor();
        var operationStore = new InMemoryOperationStore(TimeProvider.System);
        operationStore.Create(OperationType.RustUpdate);
        var control = CreateControl(executor, operationStore: operationStore);

        await Assert.ThrowsAsync<RustServerMutationInProgressException>(() => control.StartAsync());

        Assert.Empty(executor.Requests);
    }

    [Fact]
    public async Task StartAsync_WhenOxideUpdateIsPending_RejectsMutationBeforeAcquiringTheLock()
    {
        var executor = new RecordingProcessExecutor();
        var operationStore = new InMemoryOperationStore(TimeProvider.System);
        operationStore.Create(OperationType.OxideUpdate);
        var control = CreateControl(executor, operationStore: operationStore);

        await Assert.ThrowsAsync<RustServerMutationInProgressException>(() => control.StartAsync());

        Assert.Empty(executor.Requests);
    }

    private static DockerComposeServerControl CreateControl(
        IProcessExecutor executor,
        IRconClient? commandClient = null,
        RustServerControlOptions? options = null,
        InMemoryOperationStore? operationStore = null,
        IServerConfigurationStore? configurationStore = null,
        NetworkAccessSettings? networkAccessSettings = null,
        INetworkAccessRuleApplier? networkAccessRuleApplier = null) =>
        new(
            executor,
            commandClient ?? new RecordingServerCommandClient(),
            options ?? CreateOptions(),
            new RustServerRconPort(28016),
            new ServerMutationLock(),
            operationStore ?? new InMemoryOperationStore(TimeProvider.System),
            configurationStore ?? new InMemoryServerConfigurationStore(new ServerConfiguration(
                "Rust Server",
                string.Empty,
                50,
                30,
                600,
                0,
                3500)),
            new NetworkAccessOrchestrator(
                new InMemoryNetworkAccessSettingsStore(networkAccessSettings ?? NetworkAccessSettings.Default),
                networkAccessRuleApplier ?? new RecordingNetworkAccessRuleApplier()));

    private static RustServerControlOptions CreateOptions(
        TimeSpan? gracefulShutdownTimeout = null,
        TimeSpan? shutdownPollInterval = null,
        TimeSpan? readinessTimeout = null) =>
        new()
        {
            RustServerComposePath = "/opt/rust-server/compose.yml",
            GracefulShutdownTimeout = gracefulShutdownTimeout ?? TimeSpan.FromSeconds(30),
            ShutdownPollInterval = shutdownPollInterval ?? TimeSpan.FromMilliseconds(500),
            ReadinessTimeout = readinessTimeout ?? TimeSpan.FromSeconds(2)
        };

    private sealed class RecordingServerCommandClient : IRconClient
    {
        private readonly Exception? _exception;

        public RecordingServerCommandClient(Exception? exception = null)
        {
            _exception = exception;
        }

        public List<string> Commands { get; } = [];

        public Task<RconCommandResult> ExecuteCommandAsync(
            string command,
            CancellationToken cancellationToken = default)
        {
            Commands.Add(command);

            return _exception is null
                ? Task.FromResult(new RconCommandResult(string.Empty))
                : Task.FromException<RconCommandResult>(_exception);
        }

        public Task<RconConnectionState> GetConnectionStatusAsync(
            CancellationToken cancellationToken = default)
        {
            Commands.Add("status");

            return _exception is null
                ? Task.FromResult(RconConnectionState.Connected)
                : Task.FromException<RconConnectionState>(_exception);
        }
    }

    private sealed class InMemoryNetworkAccessSettingsStore(NetworkAccessSettings settings) : INetworkAccessSettingsStore
    {
        public NetworkAccessSettings Get() => settings;

        public void Save(NetworkAccessSettings newSettings) => settings = newSettings;
    }

    private sealed class RecordingNetworkAccessRuleApplier : INetworkAccessRuleApplier
    {
        public List<NetworkAccessSettings> Applied { get; } = [];

        public NetworkAccessRuleApplicationException? Failure { get; init; }

        public Task ApplyAsync(NetworkAccessSettings settings, CancellationToken cancellationToken = default)
        {
            Applied.Add(settings);
            return Failure is null ? Task.CompletedTask : Task.FromException(Failure);
        }
    }

    private sealed class RecordingProcessExecutor : IProcessExecutor
    {
        private readonly Queue<ProcessExecutionResult> _results;

        public RecordingProcessExecutor(params ProcessExecutionResult[] results)
        {
            _results = new Queue<ProcessExecutionResult>(results);
        }

        public List<ProcessExecutionRequest> Requests { get; } = [];

        public List<CancellationToken> CancellationTokens { get; } = [];

        public Task<ProcessExecutionResult> ExecuteAsync(
            ProcessExecutionRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            CancellationTokens.Add(cancellationToken);
            var result = _results.Count > 0
                ? _results.Dequeue()
                : new ProcessExecutionResult(0, string.Empty, string.Empty);
            return Task.FromResult(result);
        }
    }

    private sealed class WaitForCancellationServerCommandClient : IRconClient
    {
        public List<string> Commands { get; } = [];

        public TaskCompletionSource CommandStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool CancellationRequested { get; private set; }

        public Task<RconCommandResult> ExecuteCommandAsync(
            string command,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new RconCommandResult(string.Empty));

        public async Task<RconConnectionState> GetConnectionStatusAsync(
            CancellationToken cancellationToken = default)
        {
            Commands.Add("status");
            CommandStarted.SetResult();

            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            finally
            {
                CancellationRequested = cancellationToken.IsCancellationRequested;
            }

            return RconConnectionState.Connected;
        }
    }

    private sealed class StopFallbackProcessExecutor : IProcessExecutor
    {
        private readonly int _stopExitCode;
        private bool _stopped;

        public StopFallbackProcessExecutor(int stopExitCode = 0)
        {
            _stopExitCode = stopExitCode;
        }

        public List<ProcessExecutionRequest> Requests { get; } = [];

        public Task<ProcessExecutionResult> ExecuteAsync(
            ProcessExecutionRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);

            if (request.Arguments.Contains("ps"))
            {
                return Task.FromResult(new ProcessExecutionResult(
                    0,
                    _stopped ? string.Empty : "container-id\n",
                    string.Empty));
            }

            if (request.Arguments.Contains("stop"))
            {
                if (_stopExitCode == 0)
                {
                    _stopped = true;
                }

                return Task.FromResult(new ProcessExecutionResult(_stopExitCode, string.Empty, string.Empty));
            }

            return Task.FromResult(new ProcessExecutionResult(0, string.Empty, string.Empty));
        }
    }

    private sealed class BlockingProcessExecutor : IProcessExecutor
    {
        private int _executionCount;

        public TaskCompletionSource FirstExecutionStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource AllowFirstExecutionToComplete { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int ExecutionCount => Volatile.Read(ref _executionCount);

        public async Task<ProcessExecutionResult> ExecuteAsync(
            ProcessExecutionRequest request,
            CancellationToken cancellationToken = default)
        {
            var executionNumber = Interlocked.Increment(ref _executionCount);
            if (executionNumber == 1)
            {
                FirstExecutionStarted.SetResult();
                await AllowFirstExecutionToComplete.Task.WaitAsync(cancellationToken);
            }

            return new ProcessExecutionResult(0, string.Empty, string.Empty);
        }
    }
}
