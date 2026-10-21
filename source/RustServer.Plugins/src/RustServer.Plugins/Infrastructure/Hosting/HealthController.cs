using Microsoft.AspNetCore.Mvc;

namespace RustServer.Plugins.Infrastructure.Hosting;

/// <summary>
/// Reports whether the Plugins service can respond to HTTP requests.
/// </summary>
[ApiController]
[Route("health")]
public sealed class HealthController : ControllerBase
{
    /// <summary>
    /// Gets the availability of the Plugins service.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult Get()
    {
        return Ok();
    }
}
