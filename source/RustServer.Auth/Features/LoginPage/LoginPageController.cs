using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace RustServer.Auth.Features.LoginPage;

[Route("login")]
[ApiController]
public sealed class LoginPageController(
    IWebHostEnvironment environment) : Controller
{
    [HttpGet]
    [AllowAnonymous]
    [EndpointSummary("Returns the RustServer login page.")]
    [Produces("text/html")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult Get()
    {
        string loginPagePath = Path.Combine(environment.WebRootPath, "index.html");

        return PhysicalFile(loginPagePath, "text/html");
    }
}
