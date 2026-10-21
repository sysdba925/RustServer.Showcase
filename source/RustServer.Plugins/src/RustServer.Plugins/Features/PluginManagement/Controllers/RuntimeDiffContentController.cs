using Microsoft.AspNetCore.Mvc;
using RustServer.Plugins.Features.PluginManagement.Baseline;
using RustServer.Plugins.Features.PluginManagement.Models.Git;
using RustServer.Plugins.Features.PluginManagement.Runtime;
using RustServer.Plugins.Features.PluginManagement.RuntimeChanges;
using RustServer.Plugins.Infrastructure.Http;

namespace RustServer.Plugins.Features.PluginManagement.Controllers;

[ApiController]
[Route("api/runtime/diff/content")]
public sealed class RuntimeDiffContentController(RuntimeDiffContentReader contentReader) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<GetRuntimeDiffContentResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status500InternalServerError)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<GetRuntimeDiffContentResponse>> GetAsync(
        [FromQuery] string repository,
        [FromQuery] string path,
        CancellationToken cancellationToken)
    {
        if (!TryParseRepository(repository, out var repositoryId) || string.IsNullOrWhiteSpace(path))
        {
            return InvalidEntry();
        }

        try
        {
            var result = await contentReader.GetAsync(repositoryId, path, cancellationToken);
            return Ok(new GetRuntimeDiffContentResponse(
                ToApiValue(result.Status),
                result.Content is null ? null : ToResponse(result.Content)));
        }
        catch (BaselineStateException)
        {
            return BaselineStateCorrupted();
        }
        catch (RuntimeDiffContentException exception)
        {
            return exception.Failure switch
            {
                RuntimeDiffContentFailure.InvalidEntry => InvalidEntry(),
                RuntimeDiffContentFailure.EntryNotFound => EntryNotFound(),
                _ => throw new ArgumentOutOfRangeException(nameof(exception.Failure), exception.Failure, "Unsupported Runtime Diff content failure.")
            };
        }
        catch (RuntimeDiffException exception)
        {
            return DependencyUnavailable(exception);
        }
    }

    private static RuntimeDiffContentResponse ToResponse(RuntimeDiffContent content) =>
        new(
            ToApiValue(content.Repository),
            content.RuntimeRelativePath,
            ToApiValue(content.Change),
            ToResponse(content.Baseline),
            ToResponse(content.Runtime));

    private static RuntimeDiffContentSideResponse ToResponse(RuntimeDiffContentSide side) =>
        new(ToApiValue(side.Kind), side.Text);

    private ObjectResult InvalidEntry() =>
        BadRequest(new ApiErrorResponse
        {
            Code = "InvalidRuntimeDiffEntry",
            Message = "Repository and path must identify a managed Runtime file."
        });

    private ObjectResult EntryNotFound() =>
        NotFound(new ApiErrorResponse
        {
            Code = "RuntimeDiffEntryNotFound",
            Message = "The requested file is not a current Runtime Diff entry."
        });

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

    private static bool TryParseRepository(string? value, out RepositoryId repository)
    {
        switch (value)
        {
            case "plugins":
                repository = RepositoryId.Plugins;
                return true;
            case "configs":
                repository = RepositoryId.Configs;
                return true;
            default:
                repository = default;
                return false;
        }
    }

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

    private static string ToApiValue(RuntimeDiffContentSideKind kind) =>
        kind switch
        {
            RuntimeDiffContentSideKind.Missing => "missing",
            RuntimeDiffContentSideKind.Text => "text",
            RuntimeDiffContentSideKind.Binary => "binary",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unsupported Runtime Diff content kind.")
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

public sealed record GetRuntimeDiffContentResponse(string Status, RuntimeDiffContentResponse? Content);

public sealed record RuntimeDiffContentResponse(
    string Repository,
    string Path,
    string Change,
    RuntimeDiffContentSideResponse Baseline,
    RuntimeDiffContentSideResponse Runtime);

public sealed record RuntimeDiffContentSideResponse(string Kind, string? Text);
