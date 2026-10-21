using RustServer.Plugins.Features.PluginManagement.Models;
using RustServer.Plugins.Features.PluginManagement.Models.Operations;
using BaselineModel = RustServer.Plugins.Features.PluginManagement.Models.Baseline;

namespace RustServer.Plugins.Features.PluginManagement.Operations;

public sealed class OperationReportTracker
{
    private readonly object sync = new();
    private readonly Action<OperationReport> save;
    private readonly List<OperationStep> steps;
    private readonly OperationType type;
    private readonly Source? source;
    private string currentPhase;
    private OperationStatus status = OperationStatus.InProgress;
    private OperationFailure? failure;
    private RollbackResult? rollback = new(RollbackStatus.NotAttempted);
    private BaselineModel? previousBaseline;
    private BaselineModel? finalBaseline;
    private IReadOnlyList<OperationGitState> finalGitStates = [];
    private IReadOnlyList<OperationFileChange> changes = [];
    private OperationSyncDetails? syncDetails;
    private readonly DateTimeOffset startedAt;
    private DateTimeOffset? completedAt;

    internal OperationReportTracker(
        OperationId id,
        OperationType type,
        Source? source,
        IReadOnlyList<string> stepNames,
        string initialPhase,
        Action<OperationReport> save)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(stepNames);
        ArgumentException.ThrowIfNullOrWhiteSpace(initialPhase);
        ArgumentNullException.ThrowIfNull(save);

        if (stepNames.Count == 0 || stepNames.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("Operation steps must contain non-empty names.", nameof(stepNames));
        }

        if (stepNames.Distinct(StringComparer.Ordinal).Count() != stepNames.Count)
        {
            throw new ArgumentException("Operation steps must have unique names.", nameof(stepNames));
        }

        Id = id;
        this.type = type;
        this.source = source;
        startedAt = DateTimeOffset.UtcNow;
        steps = stepNames.Select(name => new OperationStep(name, OperationStepStatus.NotCompleted)).ToList();
        currentPhase = initialPhase;
        this.save = save;
    }

    public OperationId Id { get; }

    internal void PublishInitial()
    {
        lock (sync)
        {
            Publish();
        }
    }

    public void SetPhase(string phase)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(phase);

        lock (sync)
        {
            currentPhase = phase;
            Publish();
        }
    }

    public void CompleteStep(string name) => SetStepStatus(name, OperationStepStatus.Completed);

    public void FailStep(string name) => SetStepStatus(name, OperationStepStatus.Failed);

    public void SetFinalGitStates(IReadOnlyList<OperationGitState> states)
    {
        ArgumentNullException.ThrowIfNull(states);

        lock (sync)
        {
            finalGitStates = states.ToArray();
            Publish();
        }
    }

    public void SetFinalBaseline(BaselineModel? previous, BaselineModel? final)
    {
        lock (sync)
        {
            previousBaseline = previous;
            finalBaseline = final;
            Publish();
        }
    }

    public void SetChanges(IReadOnlyList<OperationFileChange> operationChanges)
    {
        ArgumentNullException.ThrowIfNull(operationChanges);

        lock (sync)
        {
            changes = operationChanges.ToArray();
            Publish();
        }
    }

    public void SetSync(OperationSyncDetails? syncDetails)
    {
        lock (sync)
        {
            this.syncDetails = syncDetails;
            Publish();
        }
    }

    public void SetRollback(RollbackResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        lock (sync)
        {
            rollback = result;
            Publish();
        }
    }

    public void Succeed()
    {
        lock (sync)
        {
            status = OperationStatus.Succeeded;
            currentPhase = "completed";
            completedAt = DateTimeOffset.UtcNow;
            Publish();
        }
    }

    public void Fail(OperationFailure operationFailure)
    {
        ArgumentNullException.ThrowIfNull(operationFailure);

        lock (sync)
        {
            status = OperationStatus.Failed;
            currentPhase = operationFailure.Phase;
            failure = operationFailure;
            completedAt = DateTimeOffset.UtcNow;
            Publish();
        }
    }

    private void SetStepStatus(string name, OperationStepStatus stepStatus)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        lock (sync)
        {
            var index = steps.FindIndex(step => string.Equals(step.Name, name, StringComparison.Ordinal));
            if (index < 0)
            {
                throw new ArgumentException("Operation step is not registered for this report.", nameof(name));
            }

            steps[index] = new OperationStep(name, stepStatus);
            Publish();
        }
    }

    private void Publish() => save(
        new OperationReport(
            Id,
            type,
            status,
            currentPhase,
            source,
            steps.ToArray(),
            failure,
            rollback,
            previousBaseline,
            finalBaseline,
            finalGitStates.ToArray(),
            changes.ToArray(),
            syncDetails,
            startedAt,
            completedAt));
}
