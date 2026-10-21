using Microsoft.AspNetCore.Mvc;
using RustServer.Plugins.Features.PluginManagement.Baseline;
using RustServer.Plugins.Features.PluginManagement.Commands.ApplySource;
using RustServer.Plugins.Features.PluginManagement.Models;
using RustServer.Plugins.Features.PluginManagement.Models.Git;
using RustServer.Plugins.Features.PluginManagement.Runtime;
using RustServer.Plugins.Features.PluginManagement.SourcePreview;
using RustServer.Plugins.Infrastructure.EnvironmentState;
using RustServer.Plugins.Infrastructure.Http;
using BaselineModel = RustServer.Plugins.Features.PluginManagement.Models.Baseline;
using DomainSource = RustServer.Plugins.Features.PluginManagement.Models.Source;

namespace RustServer.Plugins.Features.PluginManagement.Controllers;

[ApiController]
[Route("api/source")]
public sealed class SourceController(
    SourcePreviewReader sourcePreviewReader,
    ApplySourceHandler applySourceHandler) : ControllerBase
{
    [HttpPost("preview")]
    [ProducesResponseType<SourcePreviewResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status500InternalServerError)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<SourcePreviewResponse>> PreviewAsync(
        TargetSourceRequest? request,
        CancellationToken cancellationToken)
    {
        if (request is null || !request.TryToDomain(out var targetSource) || targetSource is null)
        {
            return BadRequest(new ApiErrorResponse
            {
                Code = "InvalidSource",
                Message = "Source must be 'development' without a release or 'release' with a non-empty release version."
            });
        }

        try
        {
            var result = await sourcePreviewReader.GetAsync(targetSource, cancellationToken);
            var changes = result.Changes.Select(change => new SourcePreviewChangeResponse(
                ToApiValue(change.File.Repository),
                change.File.RuntimeRelativePath,
                ToApiValue(change.Change))).ToArray();

            return Ok(new SourcePreviewResponse(
                ToApiValue(result.Environment),
                ToResponse(result.CurrentSource),
                result.CurrentSourceDiagnostic is null
                    ? null
                    : new CurrentSourceDiagnosticResponse(
                        result.CurrentSourceDiagnostic.PluginsBranch,
                        result.CurrentSourceDiagnostic.ConfigsBranch),
                ToResponse(result.Baseline),
                ToResponse(result.TargetSource)!,
                new TargetGitResponse(result.PluginsHead.Value, result.ConfigsHead.Value),
                new SourcePreviewCountsResponse(
                    changes.Count(change => change.Change == "added"),
                    changes.Count(change => change.Change == "modified"),
                    changes.Count(change => change.Change == "deleted")),
                changes));
        }
        catch (EnvironmentStateException)
        {
            return EnvironmentStateCorrupted();
        }
        catch (BaselineStateException)
        {
            return BaselineStateCorrupted();
        }
        catch (SourcePreviewException exception)
        {
            return PreviewFailed(exception);
        }
    }

    [HttpPost("apply")]
    [ProducesResponseType<ApplySourceResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status500InternalServerError)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status502BadGateway)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<ApplySourceResponse>> ApplyAsync(
        TargetSourceRequest? request,
        CancellationToken cancellationToken)
    {
        if (request is null || !request.TryToDomain(out var targetSource) || targetSource is null)
        {
            return BadRequest(new ApiErrorResponse
            {
                Code = "InvalidSource",
                Message = "Source must be 'development' without a release or 'release' with a non-empty release version."
            });
        }

        try
        {
            var result = await applySourceHandler.HandleAsync(targetSource, cancellationToken);
            if (result.Rejection is not null)
            {
                return ApplyRejected(result.Rejection);
            }

            if (result.Succeeded)
            {
                return Accepted(new ApplySourceResponse(result.OperationId!.Value.ToString()));
            }

            return ApplyFailed(result);
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

    private ObjectResult PreviewFailed(SourcePreviewException exception)
    {
        var statusCode = exception.Failure switch
        {
            SourcePreviewFailure.SourceNotAllowed or
            SourcePreviewFailure.ReleaseIncomplete or
            SourcePreviewFailure.TargetSourceUnavailable => StatusCodes.Status409Conflict,
            SourcePreviewFailure.GitObjectUnavailable or
            SourcePreviewFailure.RuntimeUnavailable => StatusCodes.Status503ServiceUnavailable,
            _ => throw new ArgumentOutOfRangeException(nameof(exception.Failure), exception.Failure, "Unsupported Source Preview failure.")
        };

        return StatusCode(
            statusCode,
            new ApiErrorResponse
            {
                Code = exception.Failure switch
                {
                    SourcePreviewFailure.SourceNotAllowed => "SourceNotAllowed",
                    SourcePreviewFailure.ReleaseIncomplete => "ReleaseIncomplete",
                    SourcePreviewFailure.TargetSourceUnavailable => "TargetSourceUnavailable",
                    SourcePreviewFailure.GitObjectUnavailable => "GitRepositoryUnavailable",
                    SourcePreviewFailure.RuntimeUnavailable => "RuntimeUnavailable",
                    _ => throw new ArgumentOutOfRangeException(nameof(exception.Failure), exception.Failure, "Unsupported Source Preview failure.")
                },
                Message = exception.Message,
                Details = new Dictionary<string, object?>
                {
                    ["missingRepositories"] = exception.MissingRepositories.Select(ToApiValue).ToArray(),
                    ["repository"] = exception.Repository is null ? null : ToApiValue(exception.Repository.Value),
                    ["path"] = exception.FilePath,
                    ["runtimeStatus"] = exception.RuntimeStatus is null ? null : ToApiValue(exception.RuntimeStatus.Value)
                }
            });
    }

    private ObjectResult ApplyRejected(ApplySourceRejection rejection)
    {
        var code = rejection.Reason switch
        {
            ApplySourceRejectionReason.OperationInProgress => "OperationInProgress",
            ApplySourceRejectionReason.SourceNotAllowed => "SourceNotAllowed",
            _ => throw new ArgumentOutOfRangeException(nameof(rejection.Reason), rejection.Reason, "Unsupported Apply rejection.")
        };
        var message = rejection.Reason switch
        {
            ApplySourceRejectionReason.OperationInProgress => "Another PluginManagement mutation is already in progress.",
            ApplySourceRejectionReason.SourceNotAllowed => "The target Source is not allowed in the current Environment.",
            _ => throw new ArgumentOutOfRangeException(nameof(rejection.Reason), rejection.Reason, "Unsupported Apply rejection.")
        };
        return StatusCode(StatusCodes.Status409Conflict, new ApiErrorResponse { Code = code, Message = message });
    }

    private ObjectResult ApplyFailed(ApplySourceResult result)
    {
        var failure = result.Failure!;
        var statusCode = failure.Reason switch
        {
            ApplySourceFailureReason.GitRemoteFetchFailed => StatusCodes.Status502BadGateway,
            ApplySourceFailureReason.GitRepositoryUnavailable or
            ApplySourceFailureReason.GitRemoteMissing or
            ApplySourceFailureReason.ReleaseIncomplete or
            ApplySourceFailureReason.TargetSourceUnavailable or
            ApplySourceFailureReason.GitWorkspaceUnsafe or
            ApplySourceFailureReason.SourcePreparationFailed => StatusCodes.Status409Conflict,
            ApplySourceFailureReason.RuntimeApplyFailed or
            ApplySourceFailureReason.BaselinePersistFailed or
            ApplySourceFailureReason.OperationCanceled => StatusCodes.Status500InternalServerError,
            _ => throw new ArgumentOutOfRangeException(nameof(failure.Reason), failure.Reason, "Unsupported Apply failure.")
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
                    ["path"] = failure.FilePath,
                    ["missingRepositories"] = (failure.MissingRepositories ?? [])
                        .Select(ToApiValue)
                        .ToArray()
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

    private static SourceResponse? ToResponse(DomainSource? source) =>
        source is null ? null : new SourceResponse(ToApiValue(source.Kind), source.Release?.Version);

    private static SourcePreviewBaselineResponse? ToResponse(BaselineModel? baseline) =>
        baseline is null
            ? null
            : new SourcePreviewBaselineResponse(
                ToApiValue(baseline.Environment),
                ToApiValue(baseline.Source.Kind),
                baseline.Release?.Version,
                baseline.PluginsCommit.Value,
                baseline.ConfigsCommit.Value,
                baseline.AppliedAt);

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
            _ => throw new ArgumentOutOfRangeException(nameof(sourceKind), sourceKind, "Unsupported Source.")
        };

    private static string ToApiValue(RepositoryId repository) =>
        repository switch
        {
            RepositoryId.Plugins => "plugins",
            RepositoryId.Configs => "configs",
            _ => throw new ArgumentOutOfRangeException(nameof(repository), repository, "Unsupported repository.")
        };

    private static string ToApiValue(RuntimeChangeKind change) =>
        change switch
        {
            RuntimeChangeKind.Added => "added",
            RuntimeChangeKind.Modified => "modified",
            RuntimeChangeKind.Deleted => "deleted",
            _ => throw new ArgumentOutOfRangeException(nameof(change), change, "Unsupported Runtime change.")
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

public sealed record SourcePreviewResponse(
    string Environment,
    SourceResponse? CurrentSource,
    CurrentSourceDiagnosticResponse? CurrentSourceDiagnostic,
    SourcePreviewBaselineResponse? Baseline,
    SourceResponse TargetSource,
    TargetGitResponse TargetGit,
    SourcePreviewCountsResponse Counts,
    IReadOnlyList<SourcePreviewChangeResponse> Changes);

public sealed record SourceResponse(string Type, string? Release);

public sealed record CurrentSourceDiagnosticResponse(string? PluginsBranch, string? ConfigsBranch);

public sealed record SourcePreviewBaselineResponse(
    string Environment,
    string Source,
    string? Release,
    string PluginsCommit,
    string ConfigsCommit,
    DateTimeOffset AppliedAt);

public sealed record TargetGitResponse(string PluginsHead, string ConfigsHead);

public sealed record SourcePreviewCountsResponse(int Added, int Modified, int Deleted);

public sealed record SourcePreviewChangeResponse(string Repository, string Path, string Change);

public sealed record ApplySourceResponse(string OperationId);
