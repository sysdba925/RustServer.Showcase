using Microsoft.AspNetCore.Mvc;
using RustServer.Updater.Api.Deployments;
using RustServer.Updater.Api.Operations;

namespace RustServer.Updater.Api.Applications;

/// <summary>
/// Exposes registered applications.
/// </summary>
[ApiController]
[Route("api/applications")]
[Produces("application/json")]
public sealed class ApplicationsController(
    GetApplicationsHandler getApplicationsHandler,
    GetApplicationHandler getApplicationHandler,
    RefreshApplicationsHandler refreshApplicationsHandler,
    GetApplicationRegistrationsHandler getApplicationRegistrationsHandler,
    PrepareApplicationHandler prepareApplicationHandler,
    DeploymentApplicationHandler deploymentApplicationHandler) : ControllerBase
{
    /// <summary>Gets the durable registration definitions without runtime snapshot data.</summary>
    [HttpGet("registrations")]
    [ProducesResponseType<IReadOnlyCollection<ApplicationRegistration>>(StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyCollection<ApplicationRegistration>> GetRegistrations() =>
        Ok(getApplicationRegistrationsHandler.Handle());

    /// <summary>
    /// Gets every registered application, including applications that are not installed.
    /// </summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyCollection<ApplicationResponse>>(StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyCollection<ApplicationResponse>> GetAll() => Ok(getApplicationsHandler.Handle());

    /// <summary>
    /// Gets one registered application.
    /// </summary>
    /// <param name="name">The registered application name.</param>
    [HttpGet("{name}")]
    [ProducesResponseType<ApplicationResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<ApplicationResponse> GetByName(string name)
    {
        var application = getApplicationHandler.Handle(name);
        return application is null ? NotFound() : Ok(application);
    }

    /// <summary>
    /// Refreshes the state of all registered applications without changing them.
    /// </summary>
    [HttpPost("refresh")]
    [ProducesResponseType<IReadOnlyCollection<ApplicationResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyCollection<ApplicationResponse>>> Refresh(CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await refreshApplicationsHandler.HandleAsync(cancellationToken));
        }
        catch (GitHubRefreshUnavailableException exception)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new ProblemDetails
            {
                Title = "GitHub refresh is unavailable",
                Detail = exception.Message
            });
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

    /// <summary>
    /// Resolves, downloads, verifies, and stages one explicit release without activating it.
    /// </summary>
    [HttpPost("{name}/prepare")]
    [ProducesResponseType<OperationAcceptedResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<OperationAcceptedResponse>> Prepare(
        string name,
        [FromBody] PrepareApplicationRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var operation = await prepareApplicationHandler.HandleAsync(name, request, cancellationToken);
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
            return BadRequest(new ProblemDetails { Title = "Invalid package preparation request", Detail = exception.Message });
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

    /// <summary>Activates a previously verified package as the first installed release.</summary>
    [HttpPost("{name}/install")]
    [ProducesResponseType<OperationAcceptedResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<ActionResult<OperationAcceptedResponse>> Install(
        string name,
        [FromBody] DeploymentApplicationRequest request,
        CancellationToken cancellationToken) =>
        AcceptDeploymentAsync(() => deploymentApplicationHandler.InstallAsync(name, request, cancellationToken));

    /// <summary>Activates a previously verified package for an installed application.</summary>
    [HttpPost("{name}/update")]
    [ProducesResponseType<OperationAcceptedResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<ActionResult<OperationAcceptedResponse>> UpdateDeployment(
        string name,
        [FromBody] DeploymentApplicationRequest request,
        CancellationToken cancellationToken) =>
        AcceptDeploymentAsync(() => deploymentApplicationHandler.UpdateAsync(name, request, cancellationToken));

    /// <summary>Reactivates the previous successful release or a selected installed release.</summary>
    [HttpPost("{name}/rollback")]
    [ProducesResponseType<OperationAcceptedResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<ActionResult<OperationAcceptedResponse>> Rollback(
        string name,
        [FromBody] RollbackApplicationRequest? request,
        CancellationToken cancellationToken) =>
        AcceptDeploymentAsync(() => deploymentApplicationHandler.RollbackAsync(name, request, cancellationToken));

    private async Task<ActionResult<OperationAcceptedResponse>> AcceptDeploymentAsync(Func<Task<Operation>> accept)
    {
        try
        {
            var operation = await accept();
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
        catch (PreparedPackageNotFoundException exception)
        {
            return Conflict(new ProblemDetails { Title = "Verified staging package is unavailable", Detail = exception.Message });
        }
        catch (DeploymentActivationException exception)
        {
            return Conflict(new ProblemDetails { Title = "Deployment cannot be accepted", Detail = exception.Message });
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
