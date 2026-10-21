using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace RustServer.Auth.Features.Authentication.CurrentUser;

[ApiController]
[Route("api/auth")]
public sealed class CurrentUserController : ControllerBase
{
    private readonly CurrentUserHandler _handler;

    public CurrentUserController(CurrentUserHandler handler)
    {
        _handler = handler;
    }

    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType<CurrentUserResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Get()
    {
        var response = await _handler.HandleAsync(User);

        if (response is null)
        {
            return Unauthorized();
        }

        return Ok(response);
    }
}
