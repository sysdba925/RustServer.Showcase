using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace RustServer.Auth.Features.Authentication.Logout;

[ApiController]
[Route("api/auth")]
public sealed class LogoutController : ControllerBase
{
    private readonly LogoutHandler _handler;

    public LogoutController(LogoutHandler handler)
    {
        _handler = handler;
    }

    [HttpPost("logout")]
    [Authorize]
    [ValidateAntiForgeryToken]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Logout()
    {
        await _handler.HandleAsync();
        return NoContent();
    }
}
