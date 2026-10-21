using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using RustServer.Auth.Features.Authorization;
using RustServer.Auth.Features.Portal;

namespace RustServer.Auth.Tests.Features.Portal;

public sealed class PortalRedirectTests
{
    [Theory]
    [InlineData(RoleNames.Admin, "/admin")]
    [InlineData(RoleNames.Player, "/player")]
    public void Index_AuthenticatedUser_RedirectsByRole(string role, string expectedUrl)
    {
        var controller = new PortalRedirectController
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new System.Security.Claims.ClaimsPrincipal(
                        new System.Security.Claims.ClaimsIdentity(
                            new[] { new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, role) },
                            "test"))
                }
            }
        };

        var result = Assert.IsType<RedirectResult>(controller.Index());

        Assert.Equal(expectedUrl, result.Url);
    }
}
