namespace RustServer.Control.Domain.Wipe;

/// <summary>
/// A compact, path-free report for a completed or failed wipe operation.
/// </summary>
public sealed record WipeExecutionReport(
    bool Succeeded,
    IReadOnlyList<WipeTargetResult> Targets);

/// <summary>
/// Result for one semantic wipe target.
/// </summary>
public sealed record WipeTargetResult(
    string Target,
    WipeAction Action,
    WipeTargetOutcome Outcome,
    int AffectedItems,
    string? Error);

/// <summary>
/// The actual handling of a semantic wipe target.
/// </summary>
public enum WipeTargetOutcome
{
    Kept,
    Deleted,
    Transformed,
    Failed,
    NotExecuted
}
