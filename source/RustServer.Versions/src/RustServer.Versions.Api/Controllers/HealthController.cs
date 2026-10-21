using Microsoft.AspNetCore.Mvc;

namespace RustServer.Versions.Api.Controllers;

/// <summary>
/// Reports whether the Versions API is available to serve requests.
/// </summary>
[ApiController]
[Route("health")]
public sealed class HealthController : ControllerBase
{
    /// <summary>
    /// Gets the availability of the Versions API.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult Get()
    {
        return Ok();
    }
}
