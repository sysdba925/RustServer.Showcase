using RustServer.Plugins.Features.PluginManagement.Models.Operations;

namespace RustServer.Plugins.Features.PluginManagement.Operations;

public sealed record OperationHistoryItem(
    OperationId OperationId,
    OperationType Type,
    OperationStatus Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    int ChangedFiles)
{
    public static OperationHistoryItem FromReport(OperationReport report) =>
        new(
            report.Id,
            report.Type,
            report.Status,
            report.StartedAt,
            report.CompletedAt,
            report.Changes.Count);
}

public sealed record OperationHistoryPage(
    IReadOnlyList<OperationHistoryItem> Items,
    string? NextCursor);
