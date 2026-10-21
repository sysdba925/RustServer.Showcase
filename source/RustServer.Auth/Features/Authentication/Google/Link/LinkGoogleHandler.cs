using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Identity;
using RustServer.Auth.Infrastructure.Identity;

namespace RustServer.Auth.Features.Authentication.Google.Link;

public sealed class LinkGoogleHandler
{
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly UserManager<ApplicationUser> _userManager;

    public LinkGoogleHandler(
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager)
    {
        _signInManager = signInManager;
        _userManager = userManager;
    }

    public AuthenticationProperties CreateChallenge(
        string userId,
        string callbackUrl)
    {
        return _signInManager.ConfigureExternalAuthenticationProperties(
            GoogleDefaults.AuthenticationScheme,
            callbackUrl,
            userId);
    }

    public async Task<bool> CompleteAsync(
        System.Security.Claims.ClaimsPrincipal principal)
    {
        var user = await _userManager.GetUserAsync(principal);

        if (user is null)
        {
            return false;
        }

        // Require the expected user ID from the original challenge.
        var info = await _signInManager.GetExternalLoginInfoAsync(user.Id);

        if (info is null)
        {
            return false;
        }

        // Do not link an external account already owned by another user.
        var owner = await _userManager.FindByLoginAsync(info.LoginProvider, info.ProviderKey);

        if (owner is not null)
        {
            return owner.Id == user.Id;
        }

        var result = await _userManager.AddLoginAsync(user, info);

        return result.Succeeded;
    }
}