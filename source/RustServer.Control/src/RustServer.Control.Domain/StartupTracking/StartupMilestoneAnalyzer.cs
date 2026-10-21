using System.Text.RegularExpressions;

namespace RustServer.Control.Domain.StartupTracking;

public sealed partial class StartupMilestoneAnalyzer
{
    private static readonly StartupMilestoneState[] InitialStates =
        Enumerable.Repeat(StartupMilestoneState.Pending, StartupTrackingSchema.Version1.Milestones.Count)
            .ToArray();

    private readonly StartupMilestoneState[] _states = (StartupMilestoneState[])InitialStates.Clone();

    public bool Started => _states[^1] == StartupMilestoneState.Reached;

    public IReadOnlyList<StartupMilestoneState> States => Array.AsReadOnly(_states.ToArray());

    public void ProcessLine(string line)
    {
        ArgumentNullException.ThrowIfNull(line);

        var milestoneIndex = GetMilestoneIndex(line);
        if (milestoneIndex is null || _states[milestoneIndex.Value] == StartupMilestoneState.Skipped)
        {
            return;
        }

        for (var index = 0; index < milestoneIndex.Value; index++)
        {
            if (_states[index] == StartupMilestoneState.Pending)
            {
                _states[index] = StartupMilestoneState.Skipped;
            }
        }

        _states[milestoneIndex.Value] = StartupMilestoneState.Reached;
    }

    private static int? GetMilestoneIndex(string line)
    {
        if (EngineRegex().IsMatch(line))
        {
            return 0;
        }

        if (line.Contains("Begin MonoManager ReloadAssembly", StringComparison.Ordinal))
        {
            return 1;
        }

        if (line.Contains("Bootstrap Startup", StringComparison.Ordinal))
        {
            return 2;
        }

        if (line.Contains("Loading Scene", StringComparison.Ordinal))
        {
            return 3;
        }

        if (line.Contains("Loading Oxide Core", StringComparison.Ordinal))
        {
            return 4;
        }

        if (line.Contains("SteamServer Initialized", StringComparison.Ordinal))
        {
            return 5;
        }

        if (line.Contains("Server startup complete", StringComparison.Ordinal))
        {
            return 6;
        }

        if (BootstrapCompletedRegex().IsMatch(line))
        {
            return 7;
        }

        return line.Contains("SteamServer Connected", StringComparison.Ordinal) ? 8 : null;
    }

    [GeneratedRegex("^Initialize engine version:\\s*(.+)$", RegexOptions.CultureInvariant)]
    private static partial Regex EngineRegex();

    [GeneratedRegex("^\\[Bootstrap\\] completed in (?<seconds>[0-9]+(?:\\.[0-9]+)?)s$", RegexOptions.CultureInvariant)]
    private static partial Regex BootstrapCompletedRegex();
}
