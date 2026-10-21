using Microsoft.AspNetCore.Mvc;
using RustServer.Versions.Api.Versions;

namespace RustServer.Versions.Api.Controllers;

/// <summary>
/// Retrieves version information for Oxide.
/// </summary>
[ApiController]
[Route("api/versions/oxide")]
[Produces("application/json")]
public sealed class OxideVersionsController(IOxideVersionProvider oxideVersionProvider) : ControllerBase
{
    /// <summary>
    /// Gets the installed, running, and available Oxide versions.
    /// </summary>
    [HttpGet]
    [ProducesResponseType<OxideVersionInfo>(StatusCodes.Status200OK)]
    public async Task<ActionResult<OxideVersionInfo>> Get(CancellationToken cancellationToken)
    {
        return Ok(await oxideVersionProvider.GetVersionInfoAsync(cancellationToken));
    }
}
