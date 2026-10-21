using Microsoft.AspNetCore.Mvc;
using RustServer.Plugins.Features.PluginManagement.Commands.SwitchEnvironment;
using RustServer.Plugins.Features.PluginManagement.Models;
using RustServer.Plugins.Features.PluginManagement.SystemState;
using RustServer.Plugins.Infrastructure.EnvironmentState;
using RustServer.Plugins.Infrastructure.Http;

namespace RustServer.Plugins.Features.PluginManagement.Controllers;

[ApiController]
[Route("api/environment")]
public sealed class EnvironmentController(
    IEnvironmentStore environmentStore,
    SystemStateReader systemStateReader,
    SwitchEnvironmentHandler switchEnvironmentHandler) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<GetEnvironmentResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<GetEnvironmentResponse>> GetAsync(CancellationToken cancellationToken)
    {
        try
        {
            var environment = await environmentStore.GetAsync(cancellationToken);
            var systemState = await systemStateReader.GetAsync(cancellationToken);

            return Ok(new GetEnvironmentResponse(
                ToApiValue(environment),
                ToApiValue(systemState)));
        }
        catch (EnvironmentStateException)
        {
            return EnvironmentStateCorrupted();
        }
    }

    [HttpPut]
    [ProducesResponseType<UpdateEnvironmentResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<UpdateEnvironmentResponse>> PutAsync(
        UpdateEnvironmentRequest? request,
        CancellationToken cancellationToken)
    {
        if (!TryParseEnvironment(request?.Environment, out var environment))
        {
            return BadRequest(new ApiErrorResponse
            {
                Code = "InvalidEnvironment",
                Message = "Environment must be either 'staging' or 'production'."
            });
        }

        try
        {
            await switchEnvironmentHandler.HandleAsync(
                new SwitchEnvironmentCommand(environment),
                cancellationToken);

            return Ok(new UpdateEnvironmentResponse(ToApiValue(environment)));
        }
        catch (EnvironmentStateException)
        {
            return EnvironmentStateCorrupted();
        }
    }

    private ObjectResult EnvironmentStateCorrupted() =>
        StatusCode(
            StatusCodes.Status500InternalServerError,
            new ApiErrorResponse
            {
                Code = "EnvironmentStateCorrupted",
                Message = "The persisted environment state is corrupted.",
                Details = new Dictionary<string, object?>
                {
                    ["stateFile"] = "environment.json"
                }
            });

    private static bool TryParseEnvironment(string? value, out PluginEnvironment environment)
    {
        switch (value)
        {
            case "staging":
                environment = PluginEnvironment.Staging;
                return true;
            case "production":
                environment = PluginEnvironment.Production;
                return true;
            default:
                environment = default;
                return false;
        }
    }

    private static string ToApiValue(PluginEnvironment environment) =>
        environment switch
        {
            PluginEnvironment.Staging => "staging",
            PluginEnvironment.Production => "production",
            _ => throw new ArgumentOutOfRangeException(nameof(environment), environment, "Unsupported environment.")
        };

    private static string ToApiValue(PluginManagementSystemState systemState) =>
        systemState switch
        {
            PluginManagementSystemState.Uninitialized => "uninitialized",
            PluginManagementSystemState.Ready => "ready",
            PluginManagementSystemState.Degraded => "degraded",
            _ => throw new ArgumentOutOfRangeException(nameof(systemState), systemState, "Unsupported system state.")
        };
}

public sealed record UpdateEnvironmentRequest(string? Environment);

public sealed record GetEnvironmentResponse(string Environment, string SystemState);

public sealed record UpdateEnvironmentResponse(string Environment);
