using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace RustServer.Auth.Features.Antiforgery.GetCsrfToken;

[ApiController]
[Route("api/auth/csrf")]
public sealed class GetCsrfTokenController : ControllerBase
{
    private readonly GetCsrfTokenHandler _handler;

    public GetCsrfTokenController(GetCsrfTokenHandler handler)
    {
        _handler = handler;
    }

    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult Get()
    {
        Response.Headers.CacheControl = "no-store";

        string token = _handler.Handle(HttpContext);

        return Ok(new
        {
            token
        });
    }
}
