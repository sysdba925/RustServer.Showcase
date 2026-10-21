using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RustServer.Auth.Features.Authorization;

namespace RustServer.Auth.Features.Authentication.Google.Link;

[ApiController]
[Route("api/auth/google")]
[Authorize(Roles = RoleNames.Admin)]
public sealed class LinkGoogleController : ControllerBase
{
    private readonly LinkGoogleHandler _handler;

    public LinkGoogleController(LinkGoogleHandler handler)
    {
        _handler = handler;
    }

    [HttpGet("link")]
    [ProducesResponseType(StatusCodes.Status302Found)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError)]
    public IActionResult Link()
    {
        string? callbackUrl = Url.ActionLink(
            nameof(Callback),
            "LinkGoogle");

        if (callbackUrl is null)
        {
            return Problem("Cannot generate Google callback URL.");
        }

        string? userId = User.FindFirst(
            System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized();
        }

        var properties = _handler.CreateChallenge(
            userId,
            callbackUrl);

        return Challenge(
            properties,
            GoogleDefaults.AuthenticationScheme);
    }

    [HttpGet("link-callback")]
    [ProducesResponseType(StatusCodes.Status302Found)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Callback()
    {
        bool success = await _handler.CompleteAsync(User);

        // Clear temporary external authentication cookie.
        await HttpContext.SignOutAsync(
            Microsoft.AspNetCore.Identity.IdentityConstants.ExternalScheme);

        if (!success)
        {
            return BadRequest("Google account linking failed.");
        }

        return Redirect("/admin?googleLinked=1");
    }
}
