namespace RustServer.Control.Domain.StartupTracking;

public sealed record StartupSnapshot(
    string? SessionId,
    bool Started,
    IReadOnlyList<StartupMilestoneState> Milestones);
