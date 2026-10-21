using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using RustServer.Auth.Features.Authorization;
using RustServer.Auth.Infrastructure.Identity;

namespace RustServer.Auth.Features.Authentication.Steam.Login;

public sealed class SteamLoginHandler
{
    private const string Provider = "Steam";

    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly UserManager<ApplicationUser> _userManager;

    public SteamLoginHandler(
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager)
    {
        _signInManager = signInManager;
        _userManager = userManager;
    }

    public AuthenticationProperties CreateChallenge(string callbackUrl)
    {
        return _signInManager.ConfigureExternalAuthenticationProperties(
            Provider,
            callbackUrl);
    }

    public async Task<bool> HandleAsync()
    {
        var info = await _signInManager.GetExternalLoginInfoAsync();

        if (info is null || info.LoginProvider != Provider)
        {
            return false;
        }

        // First try to sign in an existing linked user.
        var signInResult = await _signInManager.ExternalLoginSignInAsync(
            info.LoginProvider,
            info.ProviderKey,
            isPersistent: false,
            bypassTwoFactor: false);

        if (signInResult.Succeeded)
        {
            return true;
        }

        // Existing account may be locked, blocked or require 2FA.
        // Never create another account for an already linked SteamID.
        var existingUser = await _userManager.FindByLoginAsync(
            info.LoginProvider,
            info.ProviderKey);

        if (existingUser is not null)
        {
            return false;
        }

        // SteamID is supplied by the verified Steam authentication provider.
        string? steamId = ExtractSteamId(info.ProviderKey);

        if (steamId is null)
        {
            return false;
        }

        var user = new ApplicationUser
        {
            UserName = $"steam_{steamId}"
        };

        var createResult = await _userManager.CreateAsync(user);

        if (!createResult.Succeeded)
        {
            return false;
        }

        var linkResult = await _userManager.AddLoginAsync(user, info);

        if (!linkResult.Succeeded)
        {
            // Roll back an account that could not be linked.
            await _userManager.DeleteAsync(user);
            return false;
        }

        var roleResult = await _userManager.AddToRoleAsync(
            user,
            RoleNames.Player);

        if (!roleResult.Succeeded)
        {
            await _userManager.DeleteAsync(user);
            return false;
        }

        await _signInManager.SignInAsync(
            user,
            isPersistent: false);

        return true;
    }
    private static string? ExtractSteamId(string providerKey)
    {
        if (!Uri.TryCreate(providerKey, UriKind.Absolute, out var uri))
        {
            return null;
        }

        if (uri.Scheme != Uri.UriSchemeHttps ||
            uri.Host != "steamcommunity.com" ||
            !uri.AbsolutePath.StartsWith("/openid/id/"))
        {
            return null;
        }

        string steamId = uri.AbsolutePath["/openid/id/".Length..];

        // SteamID64 contains exactly 17 decimal digits.
        if (steamId.Length != 17 ||
            !steamId.All(c => c is >= '0' and <= '9'))
        {
            return null;
        }

        return steamId;
    }
}