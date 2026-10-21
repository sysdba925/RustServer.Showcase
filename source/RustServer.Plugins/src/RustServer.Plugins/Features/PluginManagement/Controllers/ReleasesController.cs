using Microsoft.AspNetCore.Mvc;
using RustServer.Plugins.Features.PluginManagement.Baseline;
using RustServer.Plugins.Features.PluginManagement.Models;
using RustServer.Plugins.Features.PluginManagement.Models.Git;
using RustServer.Plugins.Features.PluginManagement.ReleaseDiscovery;
using RustServer.Plugins.Features.PluginManagement.RemoteGit;
using RustServer.Plugins.Infrastructure.Http;
using BaselineModel = RustServer.Plugins.Features.PluginManagement.Models.Baseline;

namespace RustServer.Plugins.Features.PluginManagement.Controllers;

[ApiController]
[Route("api/releases")]
public sealed class ReleasesController(
    ReleaseDiscoveryReader releaseDiscovery,
    IBaselineStore baselineStore) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<GetReleasesResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<GetReleasesResponse>> GetAsync(CancellationToken cancellationToken)
    {
        try
        {
            var baseline = await baselineStore.GetAsync(cancellationToken);
            var releases = await releaseDiscovery.GetAsync(cancellationToken);

            return Ok(new GetReleasesResponse(
                releases.Select(release => new ReleaseResponse(
                    release.Release.Version,
                    release.Release.BranchName,
                    ToApiValue(release.Availability),
                    IsCurrentRelease(release, baseline),
                    MatchesBaseline(release, baseline),
                    release.PluginsHead?.Value,
                    release.ConfigsHead?.Value)).ToArray()));
        }
        catch (BaselineStateException)
        {
            return BaselineStateCorrupted();
        }
        catch (ReleaseDiscoveryException exception)
        {
            return ReleaseDiscoveryFailed(exception);
        }
    }

    private ObjectResult ReleaseDiscoveryFailed(ReleaseDiscoveryException exception) =>
        StatusCode(
            StatusCodes.Status409Conflict,
            new ApiErrorResponse
            {
                Code = exception.Diagnostic == RemoteGitDiagnostic.RemoteMissing
                    ? "GitRemoteMissing"
                    : "GitRepositoryUnavailable",
                Message = exception.Diagnostic == RemoteGitDiagnostic.RemoteMissing
                    ? "The canonical Git remote is not configured."
                    : "The local Git repository is unavailable for release discovery.",
                Details = new Dictionary<string, object?>
                {
                    ["repository"] = ToApiValue(exception.Repository),
                    ["phase"] = "releaseDiscovery",
                    ["diagnostic"] = exception.Diagnostic is null
                        ? "gitUnavailable"
                        : ToApiValue(exception.Diagnostic.Value)
                }
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

    private static bool IsCurrentRelease(ReleaseDiscoveryEntry release, BaselineModel? baseline) =>
        baseline?.Source.Kind == SourceKind.Release &&
        string.Equals(
            baseline.Release?.Version,
            release.Release.Version,
            StringComparison.Ordinal);

    private static bool? MatchesBaseline(ReleaseDiscoveryEntry release, BaselineModel? baseline)
    {
        if (!IsCurrentRelease(release, baseline) ||
            release.Availability != ReleaseAvailability.Complete)
        {
            return null;
        }

        return release.PluginsHead == baseline!.PluginsCommit &&
               release.ConfigsHead == baseline.ConfigsCommit;
    }

    private static string ToApiValue(ReleaseAvailability availability) =>
        availability switch
        {
            ReleaseAvailability.Complete => "complete",
            ReleaseAvailability.Incomplete => "incomplete",
            _ => throw new ArgumentOutOfRangeException(nameof(availability), availability, "Unsupported release availability.")
        };

    private static string ToApiValue(RepositoryId repository) =>
        repository switch
        {
            RepositoryId.Plugins => "plugins",
            RepositoryId.Configs => "configs",
            _ => throw new ArgumentOutOfRangeException(nameof(repository), repository, "Unsupported repository.")
        };

    private static string ToApiValue(RemoteGitDiagnostic diagnostic) =>
        diagnostic switch
        {
            RemoteGitDiagnostic.WorkspaceMissing => "workspaceMissing",
            RemoteGitDiagnostic.NotRepository => "notRepository",
            RemoteGitDiagnostic.RemoteMissing => "remoteMissing",
            RemoteGitDiagnostic.TrackingRemoteUnsupported => "trackingRemoteUnsupported",
            RemoteGitDiagnostic.TrackingReferenceMissing => "trackingReferenceMissing",
            _ => "gitUnavailable"
        };
}

public sealed record GetReleasesResponse(IReadOnlyList<ReleaseResponse> Releases);

public sealed record ReleaseResponse(
    string Version,
    string Branch,
    string Status,
    bool Current,
    bool? MatchesBaseline,
    string? PluginsHead,
    string? ConfigsHead);
