namespace RustServer.Updater.Api.Applications;

/// <summary>
/// Provides the runtime application registrations and their current in-memory snapshots.
/// </summary>
public interface IApplicationRegistry
{
    IReadOnlyCollection<ApplicationRegistration> GetAll();

    ApplicationRegistration? GetByName(string name);

    IReadOnlyCollection<ApplicationSnapshot> GetSnapshots();

    ApplicationSnapshot? GetSnapshotByName(string name);

    /// <summary>Atomically publishes supplied snapshots for the current registry view.</summary>
    void ReplaceSnapshots(IReadOnlyCollection<ApplicationSnapshot> snapshots);

    /// <summary>
    /// Reloads the runtime registry. A failed reload leaves the last known-good
    /// registration set intact and raises a diagnostic exception.
    /// </summary>
    void Reload();

    /// <summary>
    /// Confirms that the on-disk registry can be read and validated now. Deployment
    /// admission calls this immediately before creating a privileged-operation request.
    /// </summary>
    void EnsureConfigurationIsCurrent();
}
