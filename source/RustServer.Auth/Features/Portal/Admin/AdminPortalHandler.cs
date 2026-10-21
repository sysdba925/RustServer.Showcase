using Microsoft.AspNetCore.Identity;
using RustServer.Auth.Infrastructure.Identity;
using System.Security.Claims;

namespace RustServer.Auth.Features.Portal.Admin;

public sealed class AdminPortalHandler(
    UserManager<ApplicationUser> userManager)
{
    public async Task<bool> CanAccessAsync(
        ClaimsPrincipal principal)
    {
        if (principal.Identity?.IsAuthenticated != true)
        {
            return false;
        }

        var user = await userManager.GetUserAsync(principal);

        if (user is null)
        {
            return false;
        }

        return await userManager.IsInRoleAsync(user, "Admin");
    }
}