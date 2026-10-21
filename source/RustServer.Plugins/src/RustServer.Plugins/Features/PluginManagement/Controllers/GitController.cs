using Microsoft.AspNetCore.Mvc;
using RustServer.Plugins.Features.PluginManagement.Commands.FetchGit;
using RustServer.Plugins.Features.PluginManagement.Commands.GitSync;
using RustServer.Plugins.Features.PluginManagement.Git;
using RustServer.Plugins.Features.PluginManagement.LocalGit;
using RustServer.Plugins.Features.PluginManagement.RemoteGit;
using RustServer.Plugins.Infrastructure.Http;

namespace RustServer.Plugins.Features.PluginManagement.Controllers;

[ApiController]
[Route("api/git")]
public sealed class GitController(
    GitStatusReader gitStatusReader,
    FetchGitHandler fetchGitHandler,
    GitSyncHandler gitSyncHandler) : ControllerBase
{
    [HttpGet("status")]
    [ProducesResponseType<GetGitStatusResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<GetGitStatusResponse>> GetStatusAsync(CancellationToken cancellationToken)
    {
        var status = await gitStatusReader.GetAsync(cancellationToken);

        return Ok(new GetGitStatusResponse(
            ToApiValue(status.Status),
            ToResponse(status.Plugins),
            ToResponse(status.Configs),
            status.LastFetchAt));
    }

    [HttpPost("fetch")]
    [ProducesResponseType<FetchGitResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status500InternalServerError)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status503ServiceUnavailable)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<FetchGitResponse>> FetchAsync(CancellationToken cancellationToken)
    {
        var result = await fetchGitHandler.HandleAsync(cancellationToken);
        if (result.Rejection is not null)
        {
            return FetchRejected(result.Rejection);
        }

        if (result.Succeeded)
        {
            return Accepted(new FetchGitResponse(result.OperationId!.Value.ToString()));
        }

        return FetchFailed(result);
    }

    [HttpPost("sync")]
    [ProducesResponseType<GitSyncResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status500InternalServerError)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status502BadGateway)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<GitSyncResponse>> SyncAsync(
        GitSyncRequest? request,
        CancellationToken cancellationToken)
    {
        if (!TryParseRepository(request?.Repository, out var repository))
        {
            return BadRequest(new ApiErrorResponse
            {
                Code = "InvalidGitRepository",
                Message = "The Git repository must be plugins or configs."
            });
        }

        var result = await gitSyncHandler.HandleAsync(new GitSyncCommand(repository), cancellationToken);
        if (result.Rejection is not null)
        {
            return SyncRejected(result.Rejection);
        }

        if (result.Succeeded)
        {
            return Accepted(new GitSyncResponse(result.OperationId!.Value.ToString()));
        }

        return SyncFailed(result);
    }

    private ObjectResult FetchFailed(FetchGitResult result)
    {
        var failure = result.Failure!;
        var statusCode = failure.Reason switch
        {
            FetchGitFailureReason.RepositoryUnavailable or FetchGitFailureReason.RemoteMissing =>
                StatusCodes.Status409Conflict,
            FetchGitFailureReason.OperationCanceled => StatusCodes.Status500InternalServerError,
            _ => StatusCodes.Status502BadGateway
        };

        return StatusCode(
            statusCode,
            new ApiErrorResponse
            {
                Code = failure.ErrorCode,
                Message = failure.ErrorMessage,
                Details = new Dictionary<string, object?>
                {
                    ["operationId"] = result.OperationId!.Value.ToString(),
                    ["repository"] = ToApiValue(failure.Repository),
                    ["phase"] = failure.Phase,
                    ["plugins"] = ToFetchStatus(result, Models.Git.RepositoryId.Plugins),
                    ["configs"] = ToFetchStatus(result, Models.Git.RepositoryId.Configs)
                }
            });
    }

    private ObjectResult FetchRejected(FetchGitRejection rejection)
    {
        if (rejection.Reason is not FetchGitRejectionReason.OperationInProgress)
        {
            throw new ArgumentOutOfRangeException(nameof(rejection.Reason), rejection.Reason, "Unsupported Fetch rejection.");
        }

        return StatusCode(StatusCodes.Status409Conflict, new ApiErrorResponse
        {
            Code = "OperationInProgress",
            Message = "Another PluginManagement mutation is already in progress.",
            Details = new Dictionary<string, object?>()
        });
    }

    private ObjectResult SyncFailed(GitSyncResult result)
    {
        var failure = result.Failure!;
        var statusCode = failure.Reason switch
        {
            GitSyncFailureReason.GitRepositoryUnavailable or
            GitSyncFailureReason.GitRemoteMissing or
            GitSyncFailureReason.GitWorkspaceUnsafe or
            GitSyncFailureReason.GitRemoteChanged or
            GitSyncFailureReason.GitMergeConflict => StatusCodes.Status409Conflict,
            GitSyncFailureReason.GitRemoteFetchFailed or
            GitSyncFailureReason.GitSyncFailed => StatusCodes.Status502BadGateway,
            GitSyncFailureReason.GitMergeRecoveryFailed or
            GitSyncFailureReason.OperationCanceled => StatusCodes.Status500InternalServerError,
            _ => throw new ArgumentOutOfRangeException(nameof(failure.Reason), failure.Reason, "Unsupported Git Sync failure.")
        };

        return StatusCode(
            statusCode,
            new ApiErrorResponse
            {
                Code = failure.ErrorCode,
                Message = failure.ErrorMessage,
                Details = new Dictionary<string, object?>
                {
                    ["operationId"] = result.OperationId!.Value.ToString(),
                    ["repository"] = ToApiValue(failure.Repository),
                    ["phase"] = failure.Phase
                }
            });
    }

    private ObjectResult SyncRejected(GitSyncRejection rejection)
    {
        if (rejection.Reason is not GitSyncRejectionReason.OperationInProgress)
        {
            throw new ArgumentOutOfRangeException(nameof(rejection.Reason), rejection.Reason, "Unsupported Git Sync rejection.");
        }

        return StatusCode(StatusCodes.Status409Conflict, new ApiErrorResponse
        {
            Code = "OperationInProgress",
            Message = "Another PluginManagement mutation is already in progress.",
            Details = new Dictionary<string, object?>()
        });
    }

    private static GetGitRepositoryStatusResponse ToResponse(GitRepositoryStatusSnapshot status) =>
        new(
            status.Local?.IsRepository is true && status.Remote?.IsRemoteConfigured is true,
            status.Local?.Branch,
            status.Local?.Head?.Value,
            status.Remote?.Branch,
            status.Remote?.Head?.Value,
            ToWorkspaceValue(status),
            ToRelationValue(status.Local?.Relation),
            ToDiagnosticValue(status));

    private static string ToWorkspaceValue(GitRepositoryStatusSnapshot status) =>
        status.Local?.IsClean switch
        {
            true => "clean",
            false => "dirty",
            _ => "unknown"
        };

    private static string ToRelationValue(LocalGitRelation? relation) =>
        relation switch
        {
            LocalGitRelation.Equal => "equal",
            LocalGitRelation.Ahead => "ahead",
            LocalGitRelation.Behind => "behind",
            LocalGitRelation.Diverged => "diverged",
            _ => "unknown"
        };

    private static string? ToDiagnosticValue(GitRepositoryStatusSnapshot status)
    {
        if (!status.IsAvailable)
        {
            return "gitUnavailable";
        }

        if (status.Local?.Diagnostic is { } localDiagnostic and not LocalGitDiagnostic.None)
        {
            return ToApiValue(localDiagnostic);
        }

        if (status.Local?.Relation is LocalGitRelation.NoTracking)
        {
            return "noTracking";
        }

        return status.Remote?.Diagnostic is { } remoteDiagnostic and not RemoteGitDiagnostic.None
            ? ToApiValue(remoteDiagnostic)
            : null;
    }

    private static string? ToFetchStatus(FetchGitResult result, Models.Git.RepositoryId repository) =>
        result.RepositoryResults.SingleOrDefault(item => item.Repository == repository)?.Status switch
        {
            null => null,
            RemoteGitFetchStatus.Succeeded => "succeeded",
            RemoteGitFetchStatus.WorkspaceMissing => "workspaceMissing",
            RemoteGitFetchStatus.NotRepository => "notRepository",
            RemoteGitFetchStatus.RemoteMissing => "remoteMissing",
            RemoteGitFetchStatus.Failed => "failed",
            _ => throw new ArgumentOutOfRangeException(nameof(repository), repository, "Unsupported repository.")
        };

    private static string ToApiValue(GitReadiness readiness) =>
        readiness switch
        {
            GitReadiness.Ready => "ready",
            GitReadiness.NotConfigured => "notConfigured",
            GitReadiness.Unavailable => "unavailable",
            GitReadiness.Degraded => "degraded",
            _ => throw new ArgumentOutOfRangeException(nameof(readiness), readiness, "Unsupported Git readiness.")
        };

    private static string ToApiValue(Models.Git.RepositoryId repository) =>
        repository switch
        {
            Models.Git.RepositoryId.Plugins => "plugins",
            Models.Git.RepositoryId.Configs => "configs",
            _ => throw new ArgumentOutOfRangeException(nameof(repository), repository, "Unsupported repository.")
        };

    private static bool TryParseRepository(string? value, out Models.Git.RepositoryId repository)
    {
        repository = value switch
        {
            "plugins" => Models.Git.RepositoryId.Plugins,
            "configs" => Models.Git.RepositoryId.Configs,
            _ => default
        };

        return value is "plugins" or "configs";
    }

    private static string ToApiValue(LocalGitDiagnostic diagnostic) =>
        diagnostic switch
        {
            LocalGitDiagnostic.WorkspaceMissing => "workspaceMissing",
            LocalGitDiagnostic.NotRepository => "notRepository",
            LocalGitDiagnostic.DetachedHead => "detachedHead",
            LocalGitDiagnostic.HeadUnavailable => "headUnavailable",
            LocalGitDiagnostic.UnsupportedBranch => "unsupportedBranch",
            _ => throw new ArgumentOutOfRangeException(nameof(diagnostic), diagnostic, "Unsupported local Git diagnostic.")
        };

    private static string ToApiValue(RemoteGitDiagnostic diagnostic) =>
        diagnostic switch
        {
            RemoteGitDiagnostic.WorkspaceMissing => "workspaceMissing",
            RemoteGitDiagnostic.NotRepository => "notRepository",
            RemoteGitDiagnostic.RemoteMissing => "remoteMissing",
            RemoteGitDiagnostic.TrackingRemoteUnsupported => "trackingRemoteUnsupported",
            RemoteGitDiagnostic.TrackingReferenceMissing => "trackingReferenceMissing",
            _ => throw new ArgumentOutOfRangeException(nameof(diagnostic), diagnostic, "Unsupported remote Git diagnostic.")
        };
}

public sealed record FetchGitResponse(string OperationId);

public sealed record GitSyncRequest(string? Repository);

public sealed record GitSyncResponse(string OperationId);

public sealed record GetGitStatusResponse(
    string GitStatus,
    GetGitRepositoryStatusResponse Plugins,
    GetGitRepositoryStatusResponse Configs,
    DateTimeOffset? LastFetchAt);

public sealed record GetGitRepositoryStatusResponse(
    bool Configured,
    string? LocalBranch,
    string? LocalHead,
    string? RemoteBranch,
    string? RemoteHead,
    string Workspace,
    string Relation,
    string? Diagnostic);
