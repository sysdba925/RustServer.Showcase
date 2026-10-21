using System.Collections.Immutable;
using RustServer.Updater.Api.Applications;

namespace RustServer.Updater.Tests;

/// <summary>In-memory registry double used to isolate handler and endpoint tests.</summary>
internal sealed class InMemoryApplicationRegistry : IApplicationRegistry
{
    private ImmutableDictionary<string, ApplicationRegistration> _applications;
    private ImmutableDictionary<string, ApplicationSnapshot> _snapshots;

    public InMemoryApplicationRegistry(params ApplicationRegistration[] applications)
    {
        _applications = applications.ToImmutableDictionary(application => application.Name, StringComparer.OrdinalIgnoreCase);
        _snapshots = applications.ToImmutableDictionary(
            application => application.Name,
            application => new ApplicationSnapshot(application.Name, null, null, ApplicationStatus.Unknown),
            StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyCollection<ApplicationRegistration> GetAll() => Volatile.Read(ref _applications).Values.ToArray();

    public ApplicationRegistration? GetByName(string name) => Volatile.Read(ref _applications).GetValueOrDefault(name);

    public IReadOnlyCollection<ApplicationSnapshot> GetSnapshots() => Volatile.Read(ref _snapshots).Values.ToArray();

    public ApplicationSnapshot? GetSnapshotByName(string name) => Volatile.Read(ref _snapshots).GetValueOrDefault(name);

    public void ReplaceSnapshots(IReadOnlyCollection<ApplicationSnapshot> snapshots)
    {
        var names = Volatile.Read(ref _applications).Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (snapshots.Count != names.Count || snapshots.Any(snapshot => !names.Contains(snapshot.Name)))
        {
            throw new ArgumentException("Snapshots must contain exactly the registered applications.", nameof(snapshots));
        }

        Interlocked.Exchange(ref _snapshots, snapshots.ToImmutableDictionary(snapshot => snapshot.Name, StringComparer.OrdinalIgnoreCase));
    }

    public void Reload()
    {
        // Test double represents a stable runtime INI file.
    }

    public void EnsureConfigurationIsCurrent() => Reload();
}
