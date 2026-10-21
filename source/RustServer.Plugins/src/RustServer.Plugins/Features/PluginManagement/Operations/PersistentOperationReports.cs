using RustServer.Plugins.Features.PluginManagement.Models;
using RustServer.Plugins.Features.PluginManagement.Models.Operations;

namespace RustServer.Plugins.Features.PluginManagement.Operations;

public sealed class PersistentOperationReports(IOperationHistoryStore historyStore) : IOperationReports
{
    public OperationReportTracker Begin(
        OperationType type,
        Source? source,
        IReadOnlyList<string> stepNames,
        string initialPhase)
    {
        ArgumentNullException.ThrowIfNull(stepNames);

        var tracker = new OperationReportTracker(
            OperationId.New(),
            type,
            source,
            stepNames,
            initialPhase,
            report => historyStore.Save(report, CancellationToken.None));
        tracker.PublishInitial();
        return tracker;
    }

    public OperationReport? Get(OperationId operationId, CancellationToken cancellationToken) =>
        historyStore.Get(operationId, cancellationToken);

    public OperationHistoryPage List(int limit, string? cursor, CancellationToken cancellationToken) =>
        historyStore.List(limit, cursor, cancellationToken);
}
