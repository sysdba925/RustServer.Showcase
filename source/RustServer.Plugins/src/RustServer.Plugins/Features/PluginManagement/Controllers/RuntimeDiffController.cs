using Microsoft.AspNetCore.Mvc;
using RustServer.Plugins.Features.PluginManagement.Baseline;
using RustServer.Plugins.Features.PluginManagement.Models.Git;
using RustServer.Plugins.Features.PluginManagement.Runtime;
using RustServer.Plugins.Features.PluginManagement.RuntimeChanges;
using RustServer.Plugins.Infrastructure.Http;

namespace RustServer.Plugins.Features.PluginManagement.Controllers;

[ApiController]
[Route("api/runtime/diff")]
public sealed class RuntimeDiffController(RuntimeDiffReader runtimeDiffReader) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<GetRuntimeDiffResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status500InternalServerError)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<GetRuntimeDiffResponse>> GetAsync(CancellationToken cancellationToken)
    {
        try
        {
            var result = await runtimeDiffReader.GetAsync(cancellationToken);

            return Ok(new GetRuntimeDiffResponse(
                ToApiValue(result.Status),
                result.Changes.Select(change => new RuntimeDiffChangeResponse(
                    ToApiValue(change.File.Repository),
                    change.File.RuntimeRelativePath,
                    ToApiValue(change.Change))).ToArray()));
        }
        catch (BaselineStateException)
        {
            return BaselineStateCorrupted();
        }
        catch (RuntimeDiffException exception)
        {
            return DependencyUnavailable(exception);
        }
    }

    private ObjectResult BaselineStateCorrupted() =>
        StatusCode(
            StatusCodes.Status500InternalServerError,
            new ApiErrorResponse
            {
                Code = "BaselineStateCorrupted",
                Message = "The persisted baseline state is corrupted.",
                Details = new Dictionary<string, object?>
                {
                    ["stateFile"] = "baseline.json"
                }
            });

    private ObjectResult DependencyUnavailable(RuntimeDiffException exception) =>
        StatusCode(
            StatusCodes.Status503ServiceUnavailable,
            new ApiErrorResponse
            {
                Code = exception.Failure switch
                {
                    RuntimeDiffFailure.BaselineGitObjectUnavailable => "BaselineGitObjectUnavailable",
                    RuntimeDiffFailure.RuntimeUnavailable => "RuntimeUnavailable",
                    _ => throw new ArgumentOutOfRangeException(nameof(exception.Failure), exception.Failure, "Unsupported Runtime Diff failure.")
                },
                Message = exception.Failure switch
                {
                    RuntimeDiffFailure.BaselineGitObjectUnavailable =>
                        "The Baseline Git object is unavailable in the local workspace.",
                    RuntimeDiffFailure.RuntimeUnavailable =>
                        "Managed Runtime files are unavailable.",
                    _ => throw new ArgumentOutOfRangeException(nameof(exception.Failure), exception.Failure, "Unsupported Runtime Diff failure.")
                },
                Details = new Dictionary<string, object?>
                {
                    ["repository"] = exception.Repository is null ? null : ToApiValue(exception.Repository.Value),
                    ["runtimeStatus"] = exception.RuntimeStatus is null ? null : ToApiValue(exception.RuntimeStatus.Value)
                }
            });

    private static string ToApiValue(RuntimeDiffStatus status) =>
        status switch
        {
            RuntimeDiffStatus.NotInitialized => "notInitialized",
            RuntimeDiffStatus.Ready => "ready",
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unsupported Runtime Diff status.")
        };

    private static string ToApiValue(RuntimeChangeKind change) =>
        change switch
        {
            RuntimeChangeKind.Added => "added",
            RuntimeChangeKind.Modified => "modified",
            RuntimeChangeKind.Deleted => "deleted",
            _ => throw new ArgumentOutOfRangeException(nameof(change), change, "Unsupported Runtime change.")
        };

    private static string ToApiValue(RepositoryId repository) =>
        repository switch
        {
            RepositoryId.Plugins => "plugins",
            RepositoryId.Configs => "configs",
            _ => throw new ArgumentOutOfRangeException(nameof(repository), repository, "Unsupported repository.")
        };

    private static string ToApiValue(RuntimeAvailabilityStatus status) =>
        status switch
        {
            RuntimeAvailabilityStatus.ServerRootMissing => "serverRootMissing",
            RuntimeAvailabilityStatus.ManagedRootMissing => "managedRootMissing",
            RuntimeAvailabilityStatus.UnsafePath => "unsafePath",
            RuntimeAvailabilityStatus.Available => "available",
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unsupported Runtime availability status.")
        };
}

public sealed record GetRuntimeDiffResponse(string Status, IReadOnlyList<RuntimeDiffChangeResponse> Changes);

public sealed record RuntimeDiffChangeResponse(string Repository, string Path, string Change);
