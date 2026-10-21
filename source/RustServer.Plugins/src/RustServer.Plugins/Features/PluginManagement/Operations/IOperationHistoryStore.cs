using RustServer.Plugins.Features.PluginManagement.Models.Operations;

namespace RustServer.Plugins.Features.PluginManagement.Operations;

public interface IOperationHistoryStore
{
    void Save(OperationReport report, CancellationToken cancellationToken);

    OperationReport? Get(OperationId operationId, CancellationToken cancellationToken);

    OperationHistoryPage List(int limit, string? cursor, CancellationToken cancellationToken);

    void MarkInProgressAsInterrupted(CancellationToken cancellationToken);
}
