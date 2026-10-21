using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace RustServer.Auth.Features.Authentication.Login;

[ApiController]
[Route("api/auth")]
public sealed class LoginController : ControllerBase
{
    private readonly LoginHandler _handler;

    public LoginController(LoginHandler handler)
    {
        _handler = handler;
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login(LoginRequest request)
    {
        bool success = await _handler.HandleAsync(request);

        if (!success)
        {
            return Unauthorized();
        }

        return Ok();
    }
}
