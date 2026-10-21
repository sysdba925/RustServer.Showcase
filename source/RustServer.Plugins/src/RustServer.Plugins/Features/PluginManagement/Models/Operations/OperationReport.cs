namespace RustServer.Plugins.Features.PluginManagement.Models.Operations;

public sealed record OperationReport
{
    public OperationReport(
        OperationId id,
        OperationType type,
        OperationStatus status,
        string currentPhase,
        Source? source,
        IReadOnlyList<OperationStep> steps,
        OperationFailure? failure = null,
        RollbackResult? rollback = null,
        Baseline? previousBaseline = null,
        Baseline? finalBaseline = null,
        IReadOnlyList<OperationGitState>? finalGitStates = null,
        IReadOnlyList<OperationFileChange>? changes = null,
        OperationSyncDetails? sync = null,
        DateTimeOffset? startedAt = null,
        DateTimeOffset? completedAt = null)
    {
        ArgumentNullException.ThrowIfNull(id);

        if (string.IsNullOrWhiteSpace(currentPhase))
        {
            throw new ArgumentException("Operation phase must contain text.", nameof(currentPhase));
        }

        ArgumentNullException.ThrowIfNull(steps);

        Id = id;
        Type = type;
        Status = status;
        CurrentPhase = currentPhase;
        Source = source;
        Steps = steps;
        Failure = failure;
        Rollback = rollback;
        PreviousBaseline = previousBaseline;
        FinalBaseline = finalBaseline;
        FinalGitStates = finalGitStates ?? [];
        Changes = changes ?? [];
        Sync = sync;
        StartedAt = (startedAt ?? DateTimeOffset.UtcNow).ToUniversalTime();
        CompletedAt = completedAt?.ToUniversalTime();
    }

    public OperationId Id { get; }

    public OperationType Type { get; }

    public OperationStatus Status { get; }

    public string CurrentPhase { get; }

    public Source? Source { get; }

    public IReadOnlyList<OperationStep> Steps { get; }

    public OperationFailure? Failure { get; }

    public RollbackResult? Rollback { get; }

    public Baseline? PreviousBaseline { get; }

    public Baseline? FinalBaseline { get; }

    public IReadOnlyList<OperationGitState> FinalGitStates { get; }

    public IReadOnlyList<OperationFileChange> Changes { get; }

    public OperationSyncDetails? Sync { get; }

    public DateTimeOffset StartedAt { get; }

    public DateTimeOffset? CompletedAt { get; }

    public OperationReport MarkInterrupted(DateTimeOffset completedAt) =>
        new(
            Id,
            Type,
            OperationStatus.Interrupted,
            "interrupted",
            Source,
            Steps,
            new OperationFailure(
                "interrupted",
                "OperationInterrupted",
                "The service restarted before this operation completed."),
            Rollback,
            PreviousBaseline,
            FinalBaseline,
            FinalGitStates,
            Changes,
            Sync,
            StartedAt,
            completedAt);
}
