using Microsoft.AspNetCore.Mvc;
using RustServer.Versions.Api.Versions;

namespace RustServer.Versions.Api.Controllers;

/// <summary>
/// Retrieves version information for Rust Dedicated Server.
/// </summary>
[ApiController]
[Route("api/versions/rust")]
[Produces("application/json")]
public sealed class RustVersionsController(IRustVersionProvider rustVersionProvider) : ControllerBase
{
    /// <summary>
    /// Gets the installed, running, and available Rust versions.
    /// </summary>
    [HttpGet]
    [ProducesResponseType<RustVersionInfo>(StatusCodes.Status200OK)]
    public async Task<ActionResult<RustVersionInfo>> Get(CancellationToken cancellationToken)
    {
        return Ok(await rustVersionProvider.GetVersionInfoAsync(cancellationToken));
    }
}
