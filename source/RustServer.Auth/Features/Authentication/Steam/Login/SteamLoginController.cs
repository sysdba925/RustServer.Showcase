using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace RustServer.Auth.Features.Authentication.Steam.Login;

[ApiController]
[Route("api/auth/steam")]
public sealed class SteamLoginController : ControllerBase
{
    private const string Provider = "Steam";

    private readonly SteamLoginHandler _handler;

    public SteamLoginController(SteamLoginHandler handler)
    {
        _handler = handler;
    }

    [HttpGet("login")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status302Found)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError)]
    public IActionResult Login()
    {
        string? callbackUrl = Url.ActionLink(
            nameof(Callback),
            "SteamLogin");

        if (callbackUrl is null)
        {
            return Problem("Cannot generate Steam callback URL.");
        }

        var properties = _handler.CreateChallenge(callbackUrl);

        return Challenge(properties, Provider);
    }

    [HttpGet("login-callback")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status302Found)]
    public async Task<IActionResult> Callback()
    {
        bool success = await _handler.HandleAsync();

        await HttpContext.SignOutAsync(
            IdentityConstants.ExternalScheme);

        if (!success)
        {
            return Redirect("/login?steamError=1");
        }

        return Redirect("/portal");
    }
}
