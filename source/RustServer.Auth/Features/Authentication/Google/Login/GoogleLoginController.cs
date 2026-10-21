using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace RustServer.Auth.Features.Authentication.Google.Login;

[ApiController]
[Route("api/auth/google")]
public sealed class GoogleLoginController : ControllerBase
{
    private readonly GoogleLoginHandler _handler;

    public GoogleLoginController(GoogleLoginHandler handler)
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
            "GoogleLogin");

        if (callbackUrl is null)
        {
            return Problem("Cannot generate Google callback URL.");
        }

        var properties = _handler.CreateChallenge(callbackUrl);

        return Challenge(
            properties,
            GoogleDefaults.AuthenticationScheme);
    }

    [HttpGet("login-callback")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status302Found)]
    public async Task<IActionResult> Callback()
    {
        bool success = await _handler.HandleAsync();

        // Remove the temporary cookie used during Google authentication.
        await HttpContext.SignOutAsync(
            IdentityConstants.ExternalScheme);

        if (!success)
        {
            return Redirect("/login?googleError=1");
        }

        return Redirect("/portal");
    }
}
