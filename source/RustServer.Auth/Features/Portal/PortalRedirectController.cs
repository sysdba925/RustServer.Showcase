using Microsoft.AspNetCore.Mvc;

namespace RustServer.Auth.Features.Portal;

[Route("portal")]
public sealed class PortalRedirectController : Controller
{
    [HttpGet]
    public IActionResult Index()
    {
        // Не авторизован — возвращаем на страницу логина.
        if (User.Identity?.IsAuthenticated != true)
        {
            return Redirect("/login");
        }

        // Администратор — в панель выбора.
        if (User.IsInRole("Admin"))
        {
            return Redirect("/admin");
        }

        // Обычный игрок — в Player Portal.
        return Redirect("/player");
    }
}
