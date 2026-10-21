using Microsoft.AspNetCore.Mvc;
using RustServer.Plugins.Features.PluginManagement.LocalGit;
using RustServer.Plugins.Features.PluginManagement.Models;
using RustServer.Plugins.Features.PluginManagement.Models.Git;
using RustServer.Plugins.Features.PluginManagement.Models.Operations;
using RustServer.Plugins.Features.PluginManagement.Operations;
using RustServer.Plugins.Infrastructure.Http;
using BaselineModel = RustServer.Plugins.Features.PluginManagement.Models.Baseline;

namespace RustServer.Plugins.Features.PluginManagement.Controllers;

[ApiController]
[Route("api/operations")]
public sealed class OperationsController(IOperationReports operationReports) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<GetOperationsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status503ServiceUnavailable)]
    public ActionResult<GetOperationsResponse> List(
        [FromQuery] int? limit,
        [FromQuery] string? cursor,
        CancellationToken cancellationToken)
    {
        var requestedLimit = limit ?? OperationHistoryPagination.DefaultLimit;
        if (!OperationHistoryPagination.IsValidLimit(requestedLimit))
        {
            return BadRequest(new ApiErrorResponse
            {
                Code = "InvalidOperationHistoryQuery",
                Message = $"The operation history limit must be an integer from {OperationHistoryPagination.MinimumLimit} through {OperationHistoryPagination.MaximumLimit}."
            });
        }

        try
        {
            var page = operationReports.List(requestedLimit, cursor, cancellationToken);
            return Ok(new GetOperationsResponse(
                page.Items.Select(item => new OperationHistoryItemResponse(
                    item.OperationId.Value.ToString(),
                    ToApiValue(item.Type),
                    ToApiValue(item.Status),
                    item.StartedAt,
                    item.CompletedAt,
                    item.ChangedFiles)).ToArray(),
                page.NextCursor));
        }
        catch (OperationHistoryCursorException)
        {
            return BadRequest(new ApiErrorResponse
            {
                Code = "InvalidOperationHistoryQuery",
                Message = "The operation history cursor is invalid."
            });
        }
    }

    [HttpGet("{operationId}")]
    [ProducesResponseType<GetOperationResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status503ServiceUnavailable)]
    public ActionResult<GetOperationResponse> Get(string operationId, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(operationId, out var parsedOperationId) || parsedOperationId == Guid.Empty)
        {
            return OperationNotFound(operationId);
        }

        var report = operationReports.Get(new OperationId(parsedOperationId), cancellationToken);
        if (report is null)
        {
            return OperationNotFound(operationId);
        }

        return Ok(new GetOperationResponse(
            report.Id.Value.ToString(),
            ToApiValue(report.Type),
            ToApiValue(report.Status),
            report.CurrentPhase,
            report.Source is null
                ? null
                : new OperationSourceResponse(
                    ToApiValue(report.Source.Kind),
                    report.Source.Release?.Version),
            report.Steps.Select(step => new OperationStepResponse(
                step.Name,
                ToApiValue(step.Status))).ToArray(),
            report.Failure is null
                ? null
                : new OperationFailureResponse(
                    report.Failure.Phase,
                    report.Failure.Code,
                    report.Failure.Message,
                    report.Failure.Repository is null ? null : ToApiValue(report.Failure.Repository.Value),
                    report.Failure.FilePath),
            report.Rollback is null
                ? null
                : new OperationRollbackResponse(
                    ToApiValue(report.Rollback.Status),
                    report.Rollback.Message),
            ToCountsResponse(report.Changes),
            report.Changes.Select(change => new OperationFileChangeResponse(
                ToApiValue(change.Repository),
                change.Path,
                ToApiValue(change.Change))).ToArray(),
            report.Sync is null
                ? null
                : new OperationSyncResponse(
                    ToApiValue(report.Sync.Repository),
                    ToApiValue(report.Sync.InitialRelation),
                    ToApiValue(report.Sync.Behavior),
                    report.Sync.ManualInterventionRequired),
            report.FinalGitStates.Select(state => new OperationGitStateResponse(
                ToApiValue(state.Repository),
                state.Branch,
                state.Head?.Value,
                ToApiValue(state.Relation),
                state.Diagnostic)).ToArray(),
            ToResponse(report.PreviousBaseline),
            ToResponse(report.FinalBaseline),
            report.StartedAt,
            report.CompletedAt));
    }

    private ObjectResult OperationNotFound(string operationId) =>
        NotFound(new ApiErrorResponse
        {
            Code = "OperationNotFound",
            Message = "The requested operation report does not exist in persistent history.",
            Details = new Dictionary<string, object?>
            {
                ["operationId"] = operationId
            }
        });

    private static BaselineResponse? ToResponse(BaselineModel? baseline) =>
        baseline is null
            ? null
            : new BaselineResponse(
                ToApiValue(baseline.Environment),
                ToApiValue(baseline.Source.Kind),
                baseline.Release?.Version,
                baseline.PluginsCommit.Value,
                baseline.ConfigsCommit.Value,
                baseline.AppliedAt);

    private static OperationChangeCountsResponse ToCountsResponse(IReadOnlyList<OperationFileChange> changes) =>
        new(
            changes.Count(change => change.Change is OperationChangeKind.Added),
            changes.Count(change => change.Change is OperationChangeKind.Modified),
            changes.Count(change => change.Change is OperationChangeKind.Deleted));

    private static string ToApiValue(OperationType type) =>
        type switch
        {
            OperationType.FetchGit => "fetchGit",
            OperationType.GitSync => "gitSync",
            OperationType.ApplySource => "applySource",
            OperationType.CommitRuntime => "commitRuntime",
            OperationType.RevertRuntime => "revertRuntime",
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unsupported operation type.")
        };

    private static string ToApiValue(OperationStatus status) =>
        status switch
        {
            OperationStatus.InProgress => "inProgress",
            OperationStatus.Succeeded => "succeeded",
            OperationStatus.Failed => "failed",
            OperationStatus.Interrupted => "interrupted",
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unsupported operation status.")
        };

    private static string ToApiValue(OperationStepStatus status) =>
        status switch
        {
            OperationStepStatus.Completed => "completed",
            OperationStepStatus.Failed => "failed",
            OperationStepStatus.NotCompleted => "notCompleted",
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unsupported operation step status.")
        };

    private static string ToApiValue(RollbackStatus status) =>
        status switch
        {
            RollbackStatus.NotAttempted => "notAttempted",
            RollbackStatus.Succeeded => "succeeded",
            RollbackStatus.Failed => "failed",
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unsupported rollback status.")
        };

    private static string ToApiValue(OperationChangeKind change) =>
        change switch
        {
            OperationChangeKind.Added => "added",
            OperationChangeKind.Modified => "modified",
            OperationChangeKind.Deleted => "deleted",
            _ => throw new ArgumentOutOfRangeException(nameof(change), change, "Unsupported operation change.")
        };

    private static string ToApiValue(RepositoryId repository) =>
        repository switch
        {
            RepositoryId.Plugins => "plugins",
            RepositoryId.Configs => "configs",
            _ => throw new ArgumentOutOfRangeException(nameof(repository), repository, "Unsupported repository.")
        };

    private static string ToApiValue(LocalGitRelation relation) =>
        relation switch
        {
            LocalGitRelation.Equal => "equal",
            LocalGitRelation.Ahead => "ahead",
            LocalGitRelation.Behind => "behind",
            LocalGitRelation.Diverged => "diverged",
            LocalGitRelation.Unknown or LocalGitRelation.NoTracking => "unknown",
            _ => throw new ArgumentOutOfRangeException(nameof(relation), relation, "Unsupported Git relation.")
        };

    private static string ToApiValue(OperationSyncBehavior behavior) => behavior switch
    {
        OperationSyncBehavior.None => "none",
        OperationSyncBehavior.FastForward => "fastForward",
        OperationSyncBehavior.Push => "push",
        OperationSyncBehavior.MergeAndPush => "mergeAndPush",
        _ => throw new ArgumentOutOfRangeException(nameof(behavior), behavior, "Unsupported Git Sync behavior.")
    };

    private static string ToApiValue(PluginEnvironment environment) =>
        environment switch
        {
            PluginEnvironment.Staging => "staging",
            PluginEnvironment.Production => "production",
            _ => throw new ArgumentOutOfRangeException(nameof(environment), environment, "Unsupported environment.")
        };

    private static string ToApiValue(SourceKind sourceKind) =>
        sourceKind switch
        {
            SourceKind.Development => "development",
            SourceKind.Release => "release",
            _ => throw new ArgumentOutOfRangeException(nameof(sourceKind), sourceKind, "Unsupported source.")
        };
}

public sealed record GetOperationResponse(
    string OperationId,
    string Type,
    string Status,
    string CurrentPhase,
    OperationSourceResponse? Source,
    IReadOnlyList<OperationStepResponse> Steps,
    OperationFailureResponse? Failure,
    OperationRollbackResponse? Rollback,
    OperationChangeCountsResponse Counts,
    IReadOnlyList<OperationFileChangeResponse> Changes,
    OperationSyncResponse? Sync,
    IReadOnlyList<OperationGitStateResponse> FinalGitState,
    BaselineResponse? PreviousBaseline,
    BaselineResponse? FinalBaseline,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt);

public sealed record GetOperationsResponse(
    IReadOnlyList<OperationHistoryItemResponse> Items,
    string? NextCursor);

public sealed record OperationHistoryItemResponse(
    string OperationId,
    string Type,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    int ChangedFiles);

public sealed record OperationSourceResponse(string Type, string? Release);

public sealed record OperationStepResponse(string Name, string Status);

public sealed record OperationFailureResponse(
    string Phase,
    string Code,
    string Message,
    string? Repository,
    string? FilePath);

public sealed record OperationRollbackResponse(string Status, string? Message);

public sealed record OperationChangeCountsResponse(int Added, int Modified, int Deleted);

public sealed record OperationFileChangeResponse(string Repository, string Path, string Change);

public sealed record OperationSyncResponse(
    string Repository,
    string InitialRelation,
    string Behavior,
    bool ManualInterventionRequired);

public sealed record OperationGitStateResponse(
    string Repository,
    string? Branch,
    string? Head,
    string Relation,
    string? Diagnostic);
