using SteamKit2;

namespace RustServer.Versions.Api.Versions;

public sealed class SteamKitAvailableBuildIdReader(
    ILogger<SteamKitAvailableBuildIdReader> logger) : ISteamAvailableBuildIdReader
{
    private const uint RustDedicatedServerAppId = 258550;
    private static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(15);

    public async Task<ulong> ReadBuildIdAsync(string branch, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(branch))
        {
            throw new ArgumentException("A Steam branch must be provided.", nameof(branch));
        }

        using var timeoutCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCancellation.CancelAfter(QueryTimeout);

        var steamClient = new SteamClient();
        var callbackManager = new CallbackManager(steamClient);
        var steamUser = steamClient.GetHandler<SteamUser>()
            ?? throw new InvalidOperationException("SteamKit did not provide a SteamUser handler.");
        var steamApps = steamClient.GetHandler<SteamApps>()
            ?? throw new InvalidOperationException("SteamKit did not provide a SteamApps handler.");

        var connected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var loggedOn = new TaskCompletionSource<SteamUser.LoggedOnCallback>(TaskCreationOptions.RunContinuationsAsynchronously);
        var disconnected = new TaskCompletionSource<SteamClient.DisconnectedCallback>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var connectedSubscription = callbackManager.Subscribe<SteamClient.ConnectedCallback>(
            _ => connected.TrySetResult());
        using var loggedOnSubscription = callbackManager.Subscribe<SteamUser.LoggedOnCallback>(
            callback => loggedOn.TrySetResult(callback));
        using var disconnectedSubscription = callbackManager.Subscribe<SteamClient.DisconnectedCallback>(
            callback => disconnected.TrySetResult(callback));

        var callbackPump = RunCallbacksAsync(callbackManager, timeoutCancellation.Token);

        try
        {
            steamClient.Connect();
            await WaitForSteamOperationAsync(connected.Task, disconnected.Task, timeoutCancellation.Token);

            steamUser.LogOnAnonymous();
            var loggedOnCallback = await WaitForSteamOperationAsync(
                loggedOn.Task,
                disconnected.Task,
                timeoutCancellation.Token);

            if (loggedOnCallback.Result != EResult.OK)
            {
                throw new InvalidOperationException(
                    $"Anonymous Steam logon failed with result {loggedOnCallback.Result}.");
            }

            var resultSet = await WaitForSteamOperationAsync(
                steamApps.PICSGetProductInfo(
                    new SteamApps.PICSRequest(RustDedicatedServerAppId),
                    package: null,
                    metaDataOnly: false).ToTask(),
                disconnected.Task,
                timeoutCancellation.Token);

            if (!resultSet.Complete || resultSet.Failed || resultSet.Results is null)
            {
                throw new InvalidOperationException("Steam PICS did not complete the Rust app metadata request.");
            }

            var appInfo = resultSet.Results
                .SelectMany(result => result.Apps)
                .FirstOrDefault(app => app.Key == RustDedicatedServerAppId)
                .Value;

            if (appInfo is null)
            {
                throw new InvalidOperationException(
                    $"Steam PICS did not return metadata for AppID {RustDedicatedServerAppId}.");
            }

            return SteamPICSBuildIdExtractor.Extract(appInfo.KeyValues, branch.Trim());
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            var timeoutException = new TimeoutException(
                $"Steam metadata discovery did not complete within {QueryTimeout.TotalSeconds:0} seconds.",
                exception);
            logger.LogError(timeoutException, "Steam metadata discovery timed out.");
            throw timeoutException;
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("Steam metadata discovery was cancelled.");
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Steam metadata discovery failed.");
            throw;
        }
        finally
        {
            steamClient.Disconnect();
            await StopCallbackPumpAsync(timeoutCancellation, callbackPump);
        }
    }

    private static async Task RunCallbacksAsync(CallbackManager callbackManager, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await callbackManager.RunWaitCallbackAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private static async Task StopCallbackPumpAsync(
        CancellationTokenSource cancellation,
        Task callbackPump)
    {
        await cancellation.CancelAsync();
        await callbackPump;
    }

    private static async Task WaitForSteamOperationAsync(
        Task operation,
        Task disconnected,
        CancellationToken cancellationToken)
    {
        var completedTask = await Task.WhenAny(operation, disconnected).WaitAsync(cancellationToken);

        if (completedTask == disconnected)
        {
            throw new InvalidOperationException("The Steam connection closed before the metadata query completed.");
        }

        await operation;
    }

    private static async Task<T> WaitForSteamOperationAsync<T>(
        Task<T> operation,
        Task disconnected,
        CancellationToken cancellationToken)
    {
        var completedTask = await Task.WhenAny(operation, disconnected).WaitAsync(cancellationToken);

        if (completedTask == disconnected)
        {
            throw new InvalidOperationException("The Steam connection closed before the metadata query completed.");
        }

        return await operation;
    }
}
