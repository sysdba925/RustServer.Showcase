using RustServer.Plugins.Features.PluginManagement.Models;
using RustServer.Plugins.Features.PluginManagement.Models.Operations;
using System.Collections.Concurrent;

namespace RustServer.Plugins.Features.PluginManagement.Operations;

public sealed class InMemoryOperationReports : IOperationReports
{
    private readonly ConcurrentDictionary<Guid, OperationReport> reports = new();

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
            report => reports[report.Id.Value] = report);
        tracker.PublishInitial();

        return tracker;
    }

    public OperationReport? Get(OperationId operationId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operationId);
        cancellationToken.ThrowIfCancellationRequested();

        return reports.GetValueOrDefault(operationId.Value);
    }

    public OperationHistoryPage List(int limit, string? cursor, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new OperationHistoryPage(
            reports.Values
                .OrderByDescending(report => report.StartedAt)
                .ThenByDescending(report => report.Id.Value)
                .Take(limit)
                .Select(OperationHistoryItem.FromReport)
                .ToArray(),
            NextCursor: null);
    }
}
