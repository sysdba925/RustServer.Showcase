using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using RustServer.Auth.Features.Authentication.Steam.Link;
using RustServer.Auth.Infrastructure.Identity;
using RustServer.Auth.Tests.Infrastructure.Testing;

namespace RustServer.Auth.Tests.Features.Authentication.Steam;

public sealed class SteamLinkTests
{
    private const string SteamProviderKey =
        "https://steamcommunity.com/openid/id/76561198000000000";

    [Fact]
    public async Task HandleAsync_SteamAlreadyLinkedToAnotherUser_RejectsLinking()
    {
        await using IdentityTestHost host = await IdentityTestHost.CreateAsync();
        ApplicationUser owner = await host.CreateUserAsync("steam-owner");
        ApplicationUser target = await host.CreateUserAsync("target-admin");

        IdentityResult addLoginResult = await host.Users.AddLoginAsync(
            owner,
            new UserLoginInfo("Steam", SteamProviderKey, "Steam"));
        Assert.True(addLoginResult.Succeeded);

        TestSignInManager signInManager = TestSignInManagerFactory.Create(host);
        signInManager.ExternalLoginInfo = new ExternalLoginInfo(
            new ClaimsPrincipal(new ClaimsIdentity()),
            "Steam",
            SteamProviderKey,
            "Steam");

        var handler = new LinkSteamHandler(signInManager, host.Users);

        bool result = await handler.HandleAsync(ClaimsPrincipalFactory.Authenticated(target));

        Assert.False(result);
        Assert.Equal(target.Id, signInManager.ExpectedXsrfUserId);
        Assert.Empty(await host.Users.GetLoginsAsync(target));
    }
}
