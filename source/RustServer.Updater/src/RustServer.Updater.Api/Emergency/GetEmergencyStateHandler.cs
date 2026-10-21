using RustServer.Updater.Api.Applications;

namespace RustServer.Updater.Api.Emergency;

/// <summary>Combines the current application snapshot with the release choices permitted by the installation channel.</summary>
public sealed class GetEmergencyStateHandler(
    IApplicationRegistry applicationRegistry,
    IInstalledVersionProvider installedVersionProvider,
    IAvailableVersionProvider availableVersionProvider,
    IReleaseChannelProvider releaseChannelProvider)
{
    /// <summary>Gets the emergency-page state without changing any application.</summary>
    public async Task<EmergencyStateResponse> HandleAsync(CancellationToken cancellationToken)
    {
        var snapshots = applicationRegistry.GetSnapshots()
            .ToDictionary(snapshot => snapshot.Name, StringComparer.OrdinalIgnoreCase);
        var applications = applicationRegistry.GetAll();
        var applicationTasks = applications.Select(application => GetApplicationAsync(
            application,
            snapshots[application.Name],
            cancellationToken));

        return new EmergencyStateResponse(
            releaseChannelProvider.GetChannel(),
            await Task.WhenAll(applicationTasks));
    }

    private async Task<EmergencyApplicationResponse> GetApplicationAsync(
        ApplicationRegistration application,
        ApplicationSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var installedVersion = installedVersionProvider.GetInstalledVersion(application);

        try
        {
            return new EmergencyApplicationResponse(
                application.Name,
                installedVersion,
                snapshot.Status,
                await availableVersionProvider.GetAvailableVersionsAsync(application, cancellationToken),
                AvailableVersionsError: null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // The page must still offer rollback when GitHub or its credential is unavailable.
            return new EmergencyApplicationResponse(
                application.Name,
                installedVersion,
                snapshot.Status,
                [],
                "Available release discovery is unavailable.");
        }
    }
}
