using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using RustServer.Auth.Infrastructure.Identity;

namespace RustServer.Auth.Features.Authentication.Steam.Link;

public sealed class LinkSteamHandler
{
    private const string Provider = "Steam";

    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly UserManager<ApplicationUser> _userManager;

    public LinkSteamHandler(
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
            Provider,
            callbackUrl,
            userId);
    }

    public async Task<bool> HandleAsync(
        System.Security.Claims.ClaimsPrincipal principal)
    {
        var user = await _userManager.GetUserAsync(principal);

        if (user is null)
        {
            return false;
        }

        // Validate that this Steam response belongs to the user
        // who started the linking process.
        var info = await _signInManager.GetExternalLoginInfoAsync(user.Id);

        if (info is null || info.LoginProvider != Provider)
        {
            return false;
        }

        // Steam account must not already belong to another user.
        var owner = await _userManager.FindByLoginAsync(
            info.LoginProvider,
            info.ProviderKey);

        if (owner is not null)
        {
            return owner.Id == user.Id;
        }

        var result = await _userManager.AddLoginAsync(user, info);

        return result.Succeeded;
    }
}