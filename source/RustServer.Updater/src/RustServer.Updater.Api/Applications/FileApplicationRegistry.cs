using System.Collections.Immutable;
using Microsoft.Extensions.Options;

namespace RustServer.Updater.Api.Applications;

/// <summary>
/// Publishes registrations from the Updater-owned runtime INI file. It never writes the
/// registry; a rejected reload leaves the last complete configuration available for inspection.
/// </summary>
public sealed class FileApplicationRegistry : IApplicationRegistry
{
    private readonly string _storagePath;
    private readonly object _sync = new();
    private ImmutableDictionary<string, ApplicationRegistration> _applications;
    private ImmutableDictionary<string, ApplicationSnapshot> _snapshots;

    /// <summary>Loads the registry once during service start-up.</summary>
    public FileApplicationRegistry(IOptions<ApplicationRegistryOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _storagePath = RequireAbsolutePath(options.Value.StoragePath);
        var registrations = ApplicationRegistrationIniParser.ParseFile(_storagePath);
        _applications = ToApplicationDictionary(registrations);
        _snapshots = ToInitialSnapshotDictionary(registrations);
    }

    /// <inheritdoc />
    public IReadOnlyCollection<ApplicationRegistration> GetAll() => Volatile.Read(ref _applications).Values.ToArray();

    /// <inheritdoc />
    public ApplicationRegistration? GetByName(string name) => Volatile.Read(ref _applications).GetValueOrDefault(name);

    /// <inheritdoc />
    public IReadOnlyCollection<ApplicationSnapshot> GetSnapshots() => Volatile.Read(ref _snapshots).Values.ToArray();

    /// <inheritdoc />
    public ApplicationSnapshot? GetSnapshotByName(string name) =>
        Volatile.Read(ref _snapshots).GetValueOrDefault(name);

    /// <inheritdoc />
    public void ReplaceSnapshots(IReadOnlyCollection<ApplicationSnapshot> snapshots)
    {
        ArgumentNullException.ThrowIfNull(snapshots);
        lock (_sync)
        {
            var applications = Volatile.Read(ref _applications);
            var expectedNames = applications.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (snapshots.Select(snapshot => snapshot.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != snapshots.Count
                || snapshots.Any(snapshot => !expectedNames.Contains(snapshot.Name)))
            {
                throw new ArgumentException("Snapshots must belong to currently registered applications.", nameof(snapshots));
            }

            var supplied = snapshots.ToDictionary(snapshot => snapshot.Name, StringComparer.OrdinalIgnoreCase);
            var previous = Volatile.Read(ref _snapshots);
            var replacement = applications.Values.ToImmutableDictionary(
                application => application.Name,
                application => supplied.GetValueOrDefault(application.Name)
                    ?? previous.GetValueOrDefault(application.Name)
                    ?? UnknownSnapshot(application.Name),
                StringComparer.OrdinalIgnoreCase);
            Interlocked.Exchange(ref _snapshots, replacement);
        }
    }

    /// <inheritdoc />
    public void Reload()
    {
        // Parse completely before taking the publishing lock. If an administrator is
        // editing the file, a partial or invalid file cannot evict the last good set.
        var registrations = ApplicationRegistrationIniParser.ParseFile(_storagePath);
        var replacement = ToApplicationDictionary(registrations);
        lock (_sync)
        {
            var previousSnapshots = Volatile.Read(ref _snapshots);
            var replacementSnapshots = replacement.Values.ToImmutableDictionary(
                application => application.Name,
                application => previousSnapshots.GetValueOrDefault(application.Name)
                    ?? UnknownSnapshot(application.Name),
                StringComparer.OrdinalIgnoreCase);
            Interlocked.Exchange(ref _applications, replacement);
            Interlocked.Exchange(ref _snapshots, replacementSnapshots);
        }
    }

    /// <inheritdoc />
    public void EnsureConfigurationIsCurrent() => Reload();

    private static string RequireAbsolutePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)
            || (!path.StartsWith("/", StringComparison.Ordinal) && !Path.IsPathFullyQualified(path)))
        {
            throw new InvalidOperationException("Application registry storage path must be absolute.");
        }

        return Path.GetFullPath(path);
    }

    private static ImmutableDictionary<string, ApplicationRegistration> ToApplicationDictionary(
        IReadOnlyCollection<ApplicationRegistration> registrations) =>
        registrations.ToImmutableDictionary(application => application.Name, StringComparer.OrdinalIgnoreCase);

    private static ImmutableDictionary<string, ApplicationSnapshot> ToInitialSnapshotDictionary(
        IReadOnlyCollection<ApplicationRegistration> registrations) =>
        registrations.ToImmutableDictionary(
            application => application.Name,
            application => UnknownSnapshot(application.Name),
            StringComparer.OrdinalIgnoreCase);

    private static ApplicationSnapshot UnknownSnapshot(string name) =>
        new(name, null, null, ApplicationStatus.Unknown);
}
