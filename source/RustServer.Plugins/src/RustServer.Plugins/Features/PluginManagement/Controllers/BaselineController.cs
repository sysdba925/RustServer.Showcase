using Microsoft.AspNetCore.Mvc;
using RustServer.Plugins.Features.PluginManagement.Baseline;
using RustServer.Plugins.Features.PluginManagement.Models;
using RustServer.Plugins.Infrastructure.Http;

namespace RustServer.Plugins.Features.PluginManagement.Controllers;

[ApiController]
[Route("api/baseline")]
public sealed class BaselineController(IBaselineStore baselineStore) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<GetBaselineResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<GetBaselineResponse>> GetAsync(CancellationToken cancellationToken)
    {
        try
        {
            var baseline = await baselineStore.GetAsync(cancellationToken);
            if (baseline is null)
            {
                return Ok(new GetBaselineResponse("notInitialized", null));
            }

            return Ok(new GetBaselineResponse(
                "ready",
                new BaselineResponse(
                    ToApiValue(baseline.Environment),
                    ToApiValue(baseline.Source.Kind),
                    baseline.Release?.Version,
                    baseline.PluginsCommit.Value,
                    baseline.ConfigsCommit.Value,
                    baseline.AppliedAt)));
        }
        catch (BaselineStateException)
        {
            return BaselineStateCorrupted();
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

public sealed record GetBaselineResponse(string Status, BaselineResponse? Baseline);

public sealed record BaselineResponse(
    string Environment,
    string Source,
    string? Release,
    string PluginsCommit,
    string ConfigsCommit,
    DateTimeOffset AppliedAt);
