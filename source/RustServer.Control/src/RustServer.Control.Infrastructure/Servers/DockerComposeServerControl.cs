using RustServer.Control.Application.Operations;
using RustServer.Control.Application.NetworkAccess;
using RustServer.Control.Application.Processes;
using RustServer.Control.Application.Servers;
using RustServer.Control.Domain.Operations;
using RustServer.Control.Domain.Servers;

namespace RustServer.Control.Infrastructure.Servers;

/// <summary>
/// Controls the Rust Dedicated Server container through its dedicated Docker Compose project.
/// Docker is the source of truth for the container lifecycle state.
/// </summary>
public sealed class DockerComposeServerControl : IServerControl
{
    private const string GracefulShutdownCommand = "sv quit";
    private const int MaximumGracefulShutdownTimeoutMinutes = 5;
    private const int MaximumShutdownPollIntervalSeconds = 30;
    private const int MaximumReadinessTimeoutSeconds = 30;

    private readonly IProcessExecutor _processExecutor;
    private readonly IRconClient _rconClient;
    private readonly RustServerControlOptions _options;
    private readonly IServerMutationLock _serverMutationLock;
    private readonly IOperationStore _operationStore;
    private readonly IServerConfigurationStore _configurationStore;
    private readonly NetworkAccessOrchestrator _networkAccessOrchestrator;
    private readonly RustServerRconPort _rconPort;

    public DockerComposeServerControl(
        IProcessExecutor processExecutor,
        IRconClient rconClient,
        RustServerControlOptions options,
        RustServerRconPort rconPort,
        IServerMutationLock serverMutationLock,
        IOperationStore operationStore,
        IServerConfigurationStore configurationStore,
        NetworkAccessOrchestrator networkAccessOrchestrator)
    {
        ArgumentNullException.ThrowIfNull(processExecutor);
        ArgumentNullException.ThrowIfNull(rconClient);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(rconPort);
        ArgumentNullException.ThrowIfNull(serverMutationLock);
        ArgumentNullException.ThrowIfNull(operationStore);
        ArgumentNullException.ThrowIfNull(configurationStore);
        ArgumentNullException.ThrowIfNull(networkAccessOrchestrator);

        _processExecutor = processExecutor;
        _rconClient = rconClient;
        _options = options;
        _rconPort = rconPort;
        _serverMutationLock = serverMutationLock;
        _operationStore = operationStore;
        _configurationStore = configurationStore;
        _networkAccessOrchestrator = networkAccessOrchestrator;
    }

    public async Task<ServerStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        ValidateConfiguration();

        var isRunning = await IsContainerRunningAsync(cancellationToken);
        if (!isRunning)
        {
            return new ServerStatus(IsRunning: false, IsReady: false);
        }

        return new ServerStatus(
            IsRunning: true,
            IsReady: await IsRustServerReadyAsync(cancellationToken));
    }

    public Task<ServerStatus> StartAsync(CancellationToken cancellationToken = default)
    {
        ValidateConfiguration();
        ThrowIfServerMutationIsActive();

        return _serverMutationLock.ExecuteAsync(async () =>
        {
            ThrowIfServerMutationIsActive();

            var isRunning = await IsContainerRunningAsync(cancellationToken);
            if (isRunning)
            {
                await ReconcileNetworkAccessAsync(cancellationToken);
                return CreateLifecycleStatus(isRunning);
            }

            await _networkAccessOrchestrator.SuspendPublicAccessAsync(cancellationToken);
            var desired = _configurationStore.Get().Desired;
            await ExecuteComposeOperationAsync(
                "start",
                RustServerStartupConfiguration.ToEnvironmentVariables(desired),
                cancellationToken,
                "up",
                "-d",
                RustServerDockerCompose.ServiceName);

            var started = await IsContainerRunningAsync(cancellationToken);
            if (started)
            {
                _configurationStore.RecordEffective(desired);
                await ReconcileNetworkAccessAsync(cancellationToken);
            }

            return CreateLifecycleStatus(started);
        }, cancellationToken);
    }

    public Task<ServerStatus> StopAsync(CancellationToken cancellationToken = default)
    {
        ValidateConfiguration();
        ThrowIfServerMutationIsActive();

        return _serverMutationLock.ExecuteAsync(async () =>
        {
            ThrowIfServerMutationIsActive();

            var isRunning = await IsContainerRunningAsync(cancellationToken);
            if (!isRunning)
            {
                return CreateLifecycleStatus(isRunning);
            }

            await _networkAccessOrchestrator.SuspendPublicAccessAsync(cancellationToken);
            return await StopRunningServerAsync(cancellationToken);
        }, cancellationToken);
    }

    // A stopped or absent service is intentionally started by restart, keeping the endpoint predictable.
    public Task<ServerStatus> RestartAsync(CancellationToken cancellationToken = default)
    {
        ValidateConfiguration();
        ThrowIfServerMutationIsActive();

        return _serverMutationLock.ExecuteAsync(async () =>
        {
            ThrowIfServerMutationIsActive();

            await _networkAccessOrchestrator.SuspendPublicAccessAsync(cancellationToken);
            var isRunning = await IsContainerRunningAsync(cancellationToken);
            if (isRunning)
            {
                await StopRunningServerAsync(cancellationToken);
            }

            var desired = _configurationStore.Get().Desired;
            await ExecuteComposeOperationAsync(
                "restart",
                RustServerStartupConfiguration.ToEnvironmentVariables(desired),
                cancellationToken,
                "up",
                "-d",
                "--force-recreate",
                RustServerDockerCompose.ServiceName);

            var restarted = await IsContainerRunningAsync(cancellationToken);
            if (restarted)
            {
                _configurationStore.RecordEffective(desired);
                await ReconcileNetworkAccessAsync(cancellationToken);
            }

            return CreateLifecycleStatus(restarted);
        }, cancellationToken);
    }

    private async Task<bool> IsContainerRunningAsync(CancellationToken cancellationToken)
    {
        var result = await _processExecutor.ExecuteAsync(
            RustServerDockerCompose.CreateRequest(
                _options,
                _rconPort,
                "ps",
                "-q",
                RustServerDockerCompose.ServiceName),
            cancellationToken);

        ThrowIfFailed("status", result.ExitCode);

        return !string.IsNullOrWhiteSpace(result.StdOut);
    }

    private Task ReconcileNetworkAccessAsync(CancellationToken cancellationToken) =>
        _networkAccessOrchestrator.SuspendPublicAccessAsync(cancellationToken);

    private async Task<bool> IsRustServerReadyAsync(CancellationToken cancellationToken)
    {
        using var readinessTimeoutCancellationTokenSource = new CancellationTokenSource(_options.ReadinessTimeout);
        using var readinessCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            readinessTimeoutCancellationTokenSource.Token);

        try
        {
            var connectionState = await _rconClient.GetConnectionStatusAsync(
                readinessCancellationTokenSource.Token).WaitAsync(readinessCancellationTokenSource.Token);
            cancellationToken.ThrowIfCancellationRequested();

            return connectionState == RconConnectionState.Connected;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (RconClientException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return false;
        }
    }

    private async Task ExecuteComposeOperationAsync(
        string operation,
        CancellationToken cancellationToken,
        params string[] composeArguments)
    {
        await ExecuteComposeOperationAsync(operation, null, cancellationToken, composeArguments);
    }

    private async Task ExecuteComposeOperationAsync(
        string operation,
        IReadOnlyDictionary<string, string>? environmentVariables,
        CancellationToken cancellationToken,
        params string[] composeArguments)
    {
        var result = await _processExecutor.ExecuteAsync(
            RustServerDockerCompose.CreateRequest(
                _options,
                _rconPort,
                environmentVariables,
                composeArguments),
            cancellationToken);

        ThrowIfFailed(operation, result.ExitCode);
    }

    private async Task<ServerStatus> StopRunningServerAsync(CancellationToken cancellationToken)
    {
        await TryRequestGracefulShutdownAsync(cancellationToken);

        var isRunningAfterGracefulShutdown = await WaitForGracefulShutdownAsync(cancellationToken);
        if (!isRunningAfterGracefulShutdown)
        {
            return CreateLifecycleStatus(isRunningAfterGracefulShutdown);
        }

        await ExecuteComposeOperationAsync(
            "stop",
            cancellationToken,
            "stop",
            RustServerDockerCompose.ServiceName);
        return CreateLifecycleStatus(await IsContainerRunningAsync(cancellationToken));
    }

    private async Task TryRequestGracefulShutdownAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _rconClient.ExecuteCommandAsync(GracefulShutdownCommand, cancellationToken);
        }
        catch (RconClientConfigurationException)
        {
            // Docker remains available as the fallback when RCON is not configured.
        }
        catch (RconClientException)
        {
            // Rust may close WebRCON before it sends a response to "sv quit".
        }
    }

    private async Task<bool> WaitForGracefulShutdownAsync(CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + _options.GracefulShutdownTimeout;

        while (true)
        {
            var isRunning = await IsContainerRunningAsync(cancellationToken);
            if (!isRunning)
            {
                return false;
            }

            var remaining = deadline - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                return true;
            }

            await Task.Delay(
                remaining < _options.ShutdownPollInterval ? remaining : _options.ShutdownPollInterval,
                cancellationToken);
        }
    }

    private static void ThrowIfFailed(string operation, int exitCode)
    {
        if (exitCode != 0)
        {
            throw new RustServerControlException(operation, exitCode);
        }
    }

    private static ServerStatus CreateLifecycleStatus(bool isRunning) =>
        new(isRunning, IsReady: false);

    private void ThrowIfServerMutationIsActive()
    {
        if (_operationStore.HasActiveServerMutation())
        {
            throw new RustServerMutationInProgressException();
        }
    }

    private void ValidateConfiguration()
    {
        if (string.IsNullOrWhiteSpace(_options.RustServerComposePath))
        {
            throw new RustServerControlConfigurationException(
                $"Configuration value '{RustServerControlOptions.SectionName}:RustServerComposePath' is required.");
        }

        if (_options.GracefulShutdownTimeout <= TimeSpan.Zero
            || _options.GracefulShutdownTimeout > TimeSpan.FromMinutes(MaximumGracefulShutdownTimeoutMinutes))
        {
            throw new RustServerControlConfigurationException(
                $"Configuration value '{RustServerControlOptions.SectionName}:GracefulShutdownTimeout' must be greater than zero and no more than {MaximumGracefulShutdownTimeoutMinutes} minutes.");
        }

        if (_options.ShutdownPollInterval <= TimeSpan.Zero
            || _options.ShutdownPollInterval > TimeSpan.FromSeconds(MaximumShutdownPollIntervalSeconds)
            || _options.ShutdownPollInterval > _options.GracefulShutdownTimeout)
        {
            throw new RustServerControlConfigurationException(
                $"Configuration value '{RustServerControlOptions.SectionName}:ShutdownPollInterval' must be greater than zero, no more than {MaximumShutdownPollIntervalSeconds} seconds, and no greater than GracefulShutdownTimeout.");
        }

        if (_options.ReadinessTimeout <= TimeSpan.Zero
            || _options.ReadinessTimeout > TimeSpan.FromSeconds(MaximumReadinessTimeoutSeconds))
        {
            throw new RustServerControlConfigurationException(
                $"Configuration value '{RustServerControlOptions.SectionName}:ReadinessTimeout' must be greater than zero and no more than {MaximumReadinessTimeoutSeconds} seconds.");
        }
    }
}
