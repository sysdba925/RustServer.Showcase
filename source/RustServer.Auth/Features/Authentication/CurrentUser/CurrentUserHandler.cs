using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authentication.Google;
using RustServer.Auth.Infrastructure.Identity;
using System.Security.Claims;

namespace RustServer.Auth.Features.Authentication.CurrentUser;

public sealed class CurrentUserHandler
{
    private readonly UserManager<ApplicationUser> _userManager;

    public CurrentUserHandler(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    public async Task<CurrentUserResponse?> HandleAsync(
        ClaimsPrincipal principal)
    {
        var user = await _userManager.GetUserAsync(principal);

        if (user is null)
        {
            return null;
        }

        var roles = await _userManager.GetRolesAsync(user);
        var externalLogins = await _userManager.GetLoginsAsync(user);

        return new CurrentUserResponse(
            user.Id,
            user.UserName ?? string.Empty,
            roles.ToArray(),
            externalLogins.Any(login =>
                login.LoginProvider == GoogleDefaults.AuthenticationScheme),
            externalLogins.Any(login => login.LoginProvider == "Steam"));
    }
}
