using RustServer.Plugins.Features.PluginManagement.Models;
using RustServer.Plugins.Features.PluginManagement.Models.Operations;

namespace RustServer.Plugins.Features.PluginManagement.Operations;

public interface IOperationReports
{
    OperationReportTracker Begin(
        OperationType type,
        Source? source,
        IReadOnlyList<string> stepNames,
        string initialPhase);

    OperationReport? Get(OperationId operationId, CancellationToken cancellationToken = default);

    OperationHistoryPage List(int limit, string? cursor, CancellationToken cancellationToken);
}
