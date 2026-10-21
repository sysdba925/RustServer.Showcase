using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RustServer.Auth.Features.Authorization;

namespace RustServer.Auth.Features.Authentication.Verification;

[ApiController]
[Route("api/auth")]
public sealed class AuthenticationVerificationController : ControllerBase
{
    [HttpGet("verify")]
    [Authorize]
    [EndpointSummary("Verifies the Identity cookie for Nginx auth_request.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public IActionResult Verify() => Ok();

    [HttpGet("authorize/admin")]
    [Authorize(Roles = RoleNames.Admin)]
    [EndpointSummary("Verifies the Admin role from cookie claims for Nginx auth_request.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public IActionResult AuthorizeAdmin() => Ok();
}
