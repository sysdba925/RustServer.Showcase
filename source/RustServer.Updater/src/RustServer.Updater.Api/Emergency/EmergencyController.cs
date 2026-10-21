using Microsoft.AspNetCore.Mvc;
using RustServer.Updater.Api.Applications;
using RustServer.Updater.Api.Deployments;
using RustServer.Updater.Api.Operations;

namespace RustServer.Updater.Api.Emergency;

/// <summary>Backend endpoints used only by the built-in emergency administration page.</summary>
[ApiController]
[Route("api/emergency")]
[Produces("application/json")]
public sealed class EmergencyController(
    GetEmergencyStateHandler getEmergencyStateHandler,
    EmergencyUpdateApplicationHandler emergencyUpdateApplicationHandler) : ControllerBase
{
    /// <summary>Gets registered applications and all release choices permitted by the installation channel.</summary>
    [HttpGet]
    [ProducesResponseType<EmergencyStateResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<EmergencyStateResponse>> GetState(CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await getEmergencyStateHandler.HandleAsync(cancellationToken));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // Release transport and credential failures must not expose request data or credentials.
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new ProblemDetails
            {
                Title = "Available release discovery is unavailable",
                Detail = "The configured release channel could not be queried."
            });
        }
    }

    /// <summary>Prepares and activates one selected release as a single background update operation.</summary>
    [HttpPost("applications/{name}/update")]
    [ProducesResponseType<OperationAcceptedResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<OperationAcceptedResponse>> Update(
        string name,
        [FromBody] EmergencyUpdateApplicationRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var operation = await emergencyUpdateApplicationHandler.HandleAsync(name, request, cancellationToken);
            return Accepted(new OperationAcceptedResponse(operation.Id));
        }
        catch (ApplicationRegistrationNotFoundException)
        {
            return NotFound();
        }
        catch (OperationConflictException exception)
        {
            return Conflict(new ProblemDetails { Title = "Application operation is active", Detail = exception.Message });
        }
        catch (PackagePreparationException exception)
        {
            return BadRequest(new ProblemDetails { Title = "Invalid emergency update request", Detail = exception.Message });
        }
        catch (DeploymentActivationException exception)
        {
            return Conflict(new ProblemDetails { Title = "Emergency update cannot be accepted", Detail = exception.Message });
        }
        catch (ApplicationRegistryConfigurationException exception)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new ProblemDetails
            {
                Title = "Application registry configuration is invalid",
                Detail = exception.Message
            });
        }
    }
}
