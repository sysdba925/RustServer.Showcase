using Microsoft.AspNetCore.Mvc;
using RustServer.Plugins.Features.PluginManagement.Baseline;
using RustServer.Plugins.Features.PluginManagement.Commands.RevertRuntime;
using RustServer.Plugins.Features.PluginManagement.Models.Git;
using RustServer.Plugins.Features.PluginManagement.Runtime;
using RustServer.Plugins.Infrastructure.Http;

namespace RustServer.Plugins.Features.PluginManagement.Controllers;

[ApiController]
[Route("api/runtime")]
public sealed class RuntimeRevertController(
    RevertRuntimeHandler revertRuntimeHandler,
    ManagedFileMapping mapping) : ControllerBase
{
    [HttpPost("revert")]
    [ProducesResponseType<RevertRuntimeResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status500InternalServerError)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<RevertRuntimeResponse>> RevertAsync(
        RevertRuntimeRequest? request,
        CancellationToken cancellationToken)
    {
        if (!TryMapCommand(request, mapping, out var command))
        {
            return BadRequest(new ApiErrorResponse
            {
                Code = "InvalidRuntimeSelection",
                Message = "The selected Runtime changes are invalid."
            });
        }

        try
        {
            var result = await revertRuntimeHandler.HandleAsync(command, cancellationToken);
            if (result.Rejection is not null)
            {
                return Rejected(result.Rejection);
            }

            if (result.Succeeded)
            {
                return Accepted(new RevertRuntimeResponse(result.OperationId!.Value.ToString()));
            }

            return Failed(result);
        }
        catch (BaselineStateException)
        {
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                new ApiErrorResponse
                {
                    Code = "BaselineStateCorrupted",
                    Message = "The persisted baseline state is corrupted.",
                    Details = new Dictionary<string, object?> { ["stateFile"] = "baseline.json" }
                });
        }
    }

    private ObjectResult Rejected(RevertRuntimeRejection rejection)
    {
        var code = rejection.Reason switch
        {
            RevertRuntimeRejectionReason.OperationInProgress => "OperationInProgress",
            RevertRuntimeRejectionReason.BaselineNotInitialized => "BaselineNotInitialized",
            _ => throw new ArgumentOutOfRangeException(nameof(rejection.Reason), rejection.Reason, "Unsupported Runtime Revert rejection.")
        };
        var message = rejection.Reason switch
        {
            RevertRuntimeRejectionReason.OperationInProgress => "Another PluginManagement mutation is already in progress.",
            RevertRuntimeRejectionReason.BaselineNotInitialized => "A Baseline is required before Runtime changes can be reverted.",
            _ => throw new ArgumentOutOfRangeException(nameof(rejection.Reason), rejection.Reason, "Unsupported Runtime Revert rejection.")
        };
        return StatusCode(StatusCodes.Status409Conflict, new ApiErrorResponse { Code = code, Message = message });
    }

    private ObjectResult Failed(RevertRuntimeResult result)
    {
        var failure = result.Failure!;
        var statusCode = failure.Reason switch
        {
            RevertRuntimeFailureReason.InvalidRuntimeSelection => StatusCodes.Status400BadRequest,
            RevertRuntimeFailureReason.BaselineGitObjectUnavailable or
            RevertRuntimeFailureReason.RuntimeUnavailable => StatusCodes.Status503ServiceUnavailable,
            RevertRuntimeFailureReason.RuntimeRevertFailed or
            RevertRuntimeFailureReason.OperationCanceled => StatusCodes.Status500InternalServerError,
            _ => throw new ArgumentOutOfRangeException(nameof(failure.Reason), failure.Reason, "Unsupported Runtime Revert failure.")
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
                    ["repository"] = failure.Repository switch
                    {
                        null => null,
                        Models.Git.RepositoryId.Plugins => "plugins",
                        Models.Git.RepositoryId.Configs => "configs",
                        _ => throw new ArgumentOutOfRangeException(nameof(failure.Repository), failure.Repository, "Unsupported repository.")
                    },
                    ["path"] = failure.FilePath
                }
            });
    }

    private static bool TryMapCommand(
        RevertRuntimeRequest? request,
        ManagedFileMapping mapping,
        out RevertRuntimeCommand command)
    {
        if (request is null)
        {
            command = RevertRuntimeCommand.All;
            return true;
        }

        if (request.Changes is null || request.Changes.Count == 0)
        {
            command = RevertRuntimeCommand.All;
            return false;
        }

        var selections = new List<RevertRuntimeSelection>(request.Changes.Count);
        var seen = new HashSet<(RepositoryId Repository, string Path)>();
        foreach (var requestedSelection in request.Changes)
        {
            if (requestedSelection is null ||
                !TryParseRepository(requestedSelection.Repository, out var repository) ||
                string.IsNullOrWhiteSpace(requestedSelection.Path))
            {
                command = RevertRuntimeCommand.All;
                return false;
            }

            var file = mapping.FromRuntimePath(requestedSelection.Path);
            if (file is null || file.Repository != repository ||
                !string.Equals(file.RuntimeRelativePath, requestedSelection.Path, StringComparison.Ordinal) ||
                !seen.Add((repository, file.RuntimeRelativePath)))
            {
                command = RevertRuntimeCommand.All;
                return false;
            }

            selections.Add(new RevertRuntimeSelection(file));
        }

        command = new RevertRuntimeCommand(selections);
        return true;
    }

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
}

public sealed record RevertRuntimeResponse(string OperationId);

public sealed record RevertRuntimeRequest(IReadOnlyList<CommitRuntimeSelectionRequest>? Changes);
