using Microsoft.AspNetCore.Mvc;

namespace RustServer.Auth.Features.Portal.Admin;

[Route("admin")]
public sealed class AdminPortalController(
    AdminPortalHandler handler) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        if (!await handler.CanAccessAsync(User))
            return NotFound();

        return View("~/Views/Portal/Admin.cshtml");
    }
}