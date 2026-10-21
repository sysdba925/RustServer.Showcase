using System.Security.Cryptography;
using System.Text;
using RustServer.Control.Application.StartupTracking;
using RustServer.Control.Domain.StartupTracking;

namespace RustServer.Control.Infrastructure.StartupTracking;

/// <summary>
/// Builds complete startup snapshots from the current run's retained Docker logs.
/// </summary>
public sealed class StartupTrackingService : IStartupTrackingService
{
    private const string SessionPrefix = "stp_";
    private const string SessionVersion = "rust-startup-v1";

    private readonly IStartupLogSource _logSource;

    public StartupTrackingService(IStartupLogSource logSource)
    {
        ArgumentNullException.ThrowIfNull(logSource);

        _logSource = logSource;
    }

    public StartupTrackingSchema GetSchema() => StartupTrackingSchema.Version1;

    public async Task<StartupSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var run = await _logSource.ReadCurrentRunAsync(cancellationToken);
        if (run is null)
        {
            return new StartupSnapshot(
                SessionId: null,
                Started: false,
                Milestones: Enumerable
                    .Repeat(StartupMilestoneState.Pending, StartupTrackingSchema.Version1.Milestones.Count)
                    .ToArray());
        }

        var analyzer = new StartupMilestoneAnalyzer();
        foreach (var line in run.Lines)
        {
            analyzer.ProcessLine(line);
        }

        return new StartupSnapshot(
            SessionId: CreateSessionId(run.ContainerId, run.StartedAt),
            Started: analyzer.Started,
            Milestones: analyzer.States);
    }

    private static string CreateSessionId(string containerId, string startedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(containerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(startedAt);

        var canonicalValue = string.Concat(SessionVersion, '\0', containerId, '\0', startedAt);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonicalValue));
        return SessionPrefix + Convert.ToBase64String(hash)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}
