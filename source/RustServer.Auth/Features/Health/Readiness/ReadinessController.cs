using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RustServer.Auth.Infrastructure.Readiness;

namespace RustServer.Auth.Features.Health.Readiness;

[ApiController]
[Route("health/ready")]
public sealed class ReadinessController : ControllerBase
{
    private readonly IAuthReadinessProbe _readinessProbe;

    public ReadinessController(IAuthReadinessProbe readinessProbe)
    {
        _readinessProbe = readinessProbe;
    }

    [AllowAnonymous]
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> GetAsync(CancellationToken cancellationToken)
    {
        return await _readinessProbe.IsReadyAsync(cancellationToken)
            ? Ok()
            : StatusCode(StatusCodes.Status503ServiceUnavailable);
    }
}
