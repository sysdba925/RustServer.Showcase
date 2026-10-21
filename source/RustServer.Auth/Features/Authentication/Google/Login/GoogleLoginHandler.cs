using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Identity;
using RustServer.Auth.Infrastructure.Identity;

namespace RustServer.Auth.Features.Authentication.Google.Login;

public sealed class GoogleLoginHandler
{
    private readonly SignInManager<ApplicationUser> _signInManager;

    public GoogleLoginHandler(
        SignInManager<ApplicationUser> signInManager)
    {
        _signInManager = signInManager;
    }

    public AuthenticationProperties CreateChallenge(string callbackUrl)
    {
        return _signInManager.ConfigureExternalAuthenticationProperties(
            GoogleDefaults.AuthenticationScheme,
            callbackUrl);
    }

    public async Task<bool> HandleAsync()
    {
        var info = await _signInManager.GetExternalLoginInfoAsync();

        if (info is null)
        {
            return false;
        }

        // Only users with an existing linked Google account can sign in.
        var result = await _signInManager.ExternalLoginSignInAsync(
            info.LoginProvider,
            info.ProviderKey,
            isPersistent: false,
            bypassTwoFactor: false);

        return result.Succeeded;
    }
}