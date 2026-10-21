using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using RustServer.Auth.Features.Authorization;

namespace RustServer.Auth.Features.Authentication.Steam.Link;

[ApiController]
[Route("api/auth/steam")]
[Authorize(Roles = RoleNames.Admin)]
public sealed class LinkSteamController : ControllerBase
{
    private const string Provider = "Steam";

    private readonly LinkSteamHandler _handler;

    public LinkSteamController(LinkSteamHandler handler)
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
            "LinkSteam");

        if (callbackUrl is null)
        {
            return Problem("Cannot generate Steam callback URL.");
        }

        string? userId = User.FindFirst(
            System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }

        var properties = _handler.CreateChallenge(
            userId,
            callbackUrl);

        return Challenge(properties, Provider);
    }

    [HttpGet("link-callback")]
    [ProducesResponseType(StatusCodes.Status302Found)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Callback()
    {
        bool success = await _handler.HandleAsync(User);

        await HttpContext.SignOutAsync(
            IdentityConstants.ExternalScheme);

        if (!success)
        {
            return BadRequest("Steam account linking failed.");
        }

        return Redirect("/admin?steamLinked=1");
    }
}
