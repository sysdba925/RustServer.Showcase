using Microsoft.Extensions.Logging;

namespace RustServer.Updater.Api.Applications;

/// <summary>
/// Refreshes local application state and the configured GitHub Releases channels.
/// </summary>
public sealed class RefreshApplicationsHandler(
    IApplicationRegistry applicationRegistry,
    RefreshLocalApplicationStateHandler refreshLocalApplicationStateHandler,
    IAvailableVersionProvider availableVersionProvider,
    ILogger<RefreshApplicationsHandler> logger)
{
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    /// <summary>
    /// Refreshes every registered application and atomically publishes the new snapshot.
    /// </summary>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    public async Task<IReadOnlyCollection<ApplicationResponse>> HandleAsync(CancellationToken cancellationToken)
    {
        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            // Parse before reading any local state. Reload publishes only a complete,
            // valid file, so an administrator's partially written or invalid INI cannot
            // replace the current registrations.
            applicationRegistry.Reload();
            var previousSnapshots = applicationRegistry.GetSnapshots()
                .ToDictionary(snapshot => snapshot.Name, StringComparer.OrdinalIgnoreCase);
            var localSnapshots = await refreshLocalApplicationStateHandler.CollectAsync(cancellationToken);
            var refreshedSnapshots = new List<ApplicationSnapshot>(localSnapshots.Count);
            var credentialsUnavailable = false;

            foreach (var localSnapshot in localSnapshots)
            {
                var application = applicationRegistry.GetByName(localSnapshot.Name)!;
                var availableVersion = previousSnapshots.TryGetValue(application.Name, out var previousSnapshot)
                    ? previousSnapshot.AvailableVersion
                    : null;

                try
                {
                    availableVersion = await availableVersionProvider.GetAvailableVersionAsync(application, cancellationToken);
                }
                catch (GitHubCredentialUnavailableException)
                {
                    credentialsUnavailable = true;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception)
                {
                    // Do not log the exception: transport diagnostics can include request data. In particular,
                    // a GitHub credential must never be exposed through updater logs.
                    logger.LogWarning("Could not refresh the available version for application {ApplicationName}.", application.Name);
                }

                refreshedSnapshots.Add(localSnapshot with { AvailableVersion = availableVersion });
            }

            applicationRegistry.ReplaceSnapshots(refreshedSnapshots);
            if (credentialsUnavailable)
            {
                throw new GitHubRefreshUnavailableException();
            }

            return ToResponses();
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private IReadOnlyCollection<ApplicationResponse> ToResponses() => applicationRegistry
        .GetAll()
        .Select(application => GetApplicationsHandler.ToResponse(
            application,
            applicationRegistry.GetSnapshotByName(application.Name)!))
        .ToArray();
}
