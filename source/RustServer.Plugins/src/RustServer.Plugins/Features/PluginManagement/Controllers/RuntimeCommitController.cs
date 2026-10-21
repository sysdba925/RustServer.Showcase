using Microsoft.AspNetCore.Mvc;
using RustServer.Plugins.Features.PluginManagement.Baseline;
using RustServer.Plugins.Features.PluginManagement.Commands.CommitRuntime;
using RustServer.Plugins.Features.PluginManagement.Models.Git;
using RustServer.Plugins.Features.PluginManagement.Runtime;
using RustServer.Plugins.Infrastructure.EnvironmentState;
using RustServer.Plugins.Infrastructure.Http;

namespace RustServer.Plugins.Features.PluginManagement.Controllers;

[ApiController]
[Route("api/runtime")]
public sealed class RuntimeCommitController(
    CommitRuntimeHandler commitRuntimeHandler,
    ManagedFileMapping mapping) : ControllerBase
{
    [HttpPost("commit")]
    [ProducesResponseType<CommitRuntimeResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status500InternalServerError)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status502BadGateway)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<CommitRuntimeResponse>> CommitAsync(
        CommitRuntimeRequest? request,
        CancellationToken cancellationToken)
    {
        if (request?.Changes is null || request.Changes.Count == 0)
        {
            return BadRequest(new ApiErrorResponse
            {
                Code = "NoRuntimeChangesSelected",
                Message = "At least one Runtime change must be selected."
            });
        }

        if (string.IsNullOrWhiteSpace(request.Message) || !TryMapSelections(request.Changes, out var selections))
        {
            return BadRequest(new ApiErrorResponse
            {
                Code = "InvalidRuntimeSelection",
                Message = "The commit message or selected Runtime changes are invalid."
            });
        }

        try
        {
            var result = await commitRuntimeHandler.HandleAsync(
                new CommitRuntimeCommand(request.Message, selections),
                cancellationToken);
            if (result.Rejection is not null)
            {
                return Rejected(result.Rejection);
            }

            if (result.Succeeded)
            {
                return Accepted(new CommitRuntimeResponse(result.OperationId!.Value.ToString()));
            }

            return Failed(result);
        }
        catch (EnvironmentStateException)
        {
            return EnvironmentStateCorrupted();
        }
        catch (BaselineStateException)
        {
            return BaselineStateCorrupted();
        }
    }

    private bool TryMapSelections(
        IReadOnlyList<CommitRuntimeSelectionRequest> requestedSelections,
        out IReadOnlyList<CommitRuntimeSelection> selections)
    {
        var mappedSelections = new List<CommitRuntimeSelection>(requestedSelections.Count);
        var seen = new HashSet<(RepositoryId Repository, string Path)>();

        foreach (var requestedSelection in requestedSelections)
        {
            if (requestedSelection is null ||
                !TryParseRepository(requestedSelection.Repository, out var repository) ||
                string.IsNullOrWhiteSpace(requestedSelection.Path))
            {
                selections = [];
                return false;
            }

            var file = mapping.FromRuntimePath(requestedSelection.Path);
            if (file is null || file.Repository != repository ||
                !string.Equals(file.RuntimeRelativePath, requestedSelection.Path, StringComparison.Ordinal) ||
                !seen.Add((repository, file.RuntimeRelativePath)))
            {
                selections = [];
                return false;
            }

            mappedSelections.Add(new CommitRuntimeSelection(file));
        }

        selections = mappedSelections;
        return true;
    }

    private ObjectResult Rejected(CommitRuntimeRejection rejection)
    {
        var statusCode = rejection.Reason switch
        {
            CommitRuntimeRejectionReason.OperationInProgress or
            CommitRuntimeRejectionReason.CommitNotAllowedInProduction or
            CommitRuntimeRejectionReason.BaselineNotInitialized => StatusCodes.Status409Conflict,
            _ => throw new ArgumentOutOfRangeException(nameof(rejection.Reason), rejection.Reason, "Unsupported Commit Runtime rejection.")
        };

        return StatusCode(statusCode, new ApiErrorResponse
        {
            Code = rejection.Reason switch
            {
                CommitRuntimeRejectionReason.OperationInProgress => "OperationInProgress",
                CommitRuntimeRejectionReason.CommitNotAllowedInProduction => "CommitNotAllowedInProduction",
                CommitRuntimeRejectionReason.BaselineNotInitialized => "BaselineNotInitialized",
                _ => throw new ArgumentOutOfRangeException(nameof(rejection.Reason), rejection.Reason, "Unsupported Commit Runtime rejection.")
            },
            Message = rejection.Reason switch
            {
                CommitRuntimeRejectionReason.OperationInProgress => "Another PluginManagement mutation is already in progress.",
                CommitRuntimeRejectionReason.CommitNotAllowedInProduction => "Commit + Push Runtime changes is allowed only in Staging.",
                CommitRuntimeRejectionReason.BaselineNotInitialized => "A Baseline is required before Runtime changes can be committed.",
                _ => throw new ArgumentOutOfRangeException(nameof(rejection.Reason), rejection.Reason, "Unsupported Commit Runtime rejection.")
            }
        });
    }

    private ObjectResult Failed(CommitRuntimeResult result)
    {
        var failure = result.Failure!;
        var statusCode = failure.Reason switch
        {
            CommitRuntimeFailureReason.InvalidRuntimeSelection => StatusCodes.Status400BadRequest,
            CommitRuntimeFailureReason.GitRepositoryUnavailable or
            CommitRuntimeFailureReason.GitRemoteMissing or
            CommitRuntimeFailureReason.GitSourceChangedSinceBaseline or
            CommitRuntimeFailureReason.GitSynchronizationRequired or
            CommitRuntimeFailureReason.GitWorkspaceUnsafe => StatusCodes.Status409Conflict,
            CommitRuntimeFailureReason.GitRemoteFetchFailed or
            CommitRuntimeFailureReason.GitPushFailed => StatusCodes.Status502BadGateway,
            CommitRuntimeFailureReason.BaselineGitObjectUnavailable or
            CommitRuntimeFailureReason.RuntimeUnavailable => StatusCodes.Status503ServiceUnavailable,
            CommitRuntimeFailureReason.GitCommitFailed or
            CommitRuntimeFailureReason.BaselinePersistFailed or
            CommitRuntimeFailureReason.OperationCanceled => StatusCodes.Status500InternalServerError,
            _ => throw new ArgumentOutOfRangeException(nameof(failure.Reason), failure.Reason, "Unsupported Commit Runtime failure.")
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
                    ["phase"] = failure.Phase,
                    ["repository"] = failure.Repository is null ? null : ToApiValue(failure.Repository.Value),
                    ["path"] = failure.FilePath
                }
            });
    }

    private ObjectResult EnvironmentStateCorrupted() =>
        StatusCode(
            StatusCodes.Status500InternalServerError,
            new ApiErrorResponse
            {
                Code = "EnvironmentStateCorrupted",
                Message = "The persisted environment state is corrupted.",
                Details = new Dictionary<string, object?> { ["stateFile"] = "environment.json" }
            });

    private ObjectResult BaselineStateCorrupted() =>
        StatusCode(
            StatusCodes.Status500InternalServerError,
            new ApiErrorResponse
            {
                Code = "BaselineStateCorrupted",
                Message = "The persisted baseline state is corrupted.",
                Details = new Dictionary<string, object?> { ["stateFile"] = "baseline.json" }
            });

    private static bool TryParseRepository(string? value, out RepositoryId repository)
    {
        repository = value switch
        {
            "plugins" => RepositoryId.Plugins,
            "configs" => RepositoryId.Configs,
            _ => default
        };

        return value is "plugins" or "configs";
    }

    private static string ToApiValue(RepositoryId repository) => repository switch
    {
        RepositoryId.Plugins => "plugins",
        RepositoryId.Configs => "configs",
        _ => throw new ArgumentOutOfRangeException(nameof(repository), repository, "Unsupported repository.")
    };
}

public sealed record CommitRuntimeRequest(
    string? Message,
    IReadOnlyList<CommitRuntimeSelectionRequest>? Changes);

public sealed record CommitRuntimeSelectionRequest(string? Repository, string? Path);

public sealed record CommitRuntimeResponse(string OperationId);
