using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using RustServer.Auth.Features.Authorization;
using RustServer.Auth.Features.Portal.Admin;
using RustServer.Auth.Infrastructure.Identity;
using RustServer.Auth.Tests.Infrastructure.Testing;

namespace RustServer.Auth.Tests.Features.Portal;

public sealed class AdminPortalTests
{
    [Fact]
    public async Task Index_Player_RespondsWithNotFound()
    {
        await using IdentityTestHost host = await IdentityTestHost.CreateAsync();
        ApplicationUser player = await host.CreateUserAsync("player");
        Assert.True((await host.Users.AddToRoleAsync(player, RoleNames.Player)).Succeeded);

        IActionResult result = await CreateController(host, ClaimsPrincipalFactory.Authenticated(player)).Index();

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Index_Anonymous_RespondsWithNotFound()
    {
        await using IdentityTestHost host = await IdentityTestHost.CreateAsync();

        IActionResult result = await CreateController(host, ClaimsPrincipalFactory.Anonymous()).Index();

        Assert.IsType<NotFoundResult>(result);
    }

    private static AdminPortalController CreateController(
        IdentityTestHost host,
        System.Security.Claims.ClaimsPrincipal principal)
    {
        return new AdminPortalController(new AdminPortalHandler(host.Users))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = principal }
            }
        };
    }
}
