namespace RustServer.Control.Application.StartupTracking;

public sealed record StartupRunLog(
    string ContainerId,
    string StartedAt,
    IReadOnlyList<string> Lines);
