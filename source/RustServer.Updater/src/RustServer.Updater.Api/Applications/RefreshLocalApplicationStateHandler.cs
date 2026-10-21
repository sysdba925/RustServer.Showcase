namespace RustServer.Updater.Api.Applications;

/// <summary>
/// Collects local installation and systemd state without contacting release providers.
/// </summary>
public sealed class RefreshLocalApplicationStateHandler(
    IApplicationRegistry applicationRegistry,
    IInstalledVersionProvider installedVersionProvider,
    IApplicationStatusProvider applicationStatusProvider)
{
    /// <summary>
    /// Performs a local refresh and publishes the resulting snapshots.
    /// </summary>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    public async Task<IReadOnlyCollection<ApplicationSnapshot>> HandleAsync(CancellationToken cancellationToken)
    {
        var snapshots = await CollectAsync(cancellationToken);
        applicationRegistry.ReplaceSnapshots(snapshots);
        return snapshots;
    }

    /// <summary>
    /// Collects local state without changing the registry. Used to keep a full refresh atomic.
    /// </summary>
    internal async Task<IReadOnlyCollection<ApplicationSnapshot>> CollectAsync(CancellationToken cancellationToken)
    {
        var snapshots = new List<ApplicationSnapshot>();

        foreach (var application in applicationRegistry.GetAll())
        {
            var installedVersion = installedVersionProvider.GetInstalledVersion(application);
            var status = await applicationStatusProvider.GetStatusAsync(
                application,
                installedVersion is not null,
                cancellationToken);

            snapshots.Add(new ApplicationSnapshot(
                application.Name,
                installedVersion,
                AvailableVersion: null,
                status));
        }

        return snapshots;
    }
}
