using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using RustServer.Auth.Features.Authentication.Steam.Login;
using RustServer.Auth.Features.Authorization;
using RustServer.Auth.Infrastructure.Identity;
using RustServer.Auth.Tests.Infrastructure.Testing;

namespace RustServer.Auth.Tests.Features.Authentication.Steam;

public sealed class SteamLoginTests
{
    private const string SteamId = "76561198000000000";
    private const string SteamProviderKey =
        "https://steamcommunity.com/openid/id/76561198000000000";

    [Fact]
    public async Task HandleAsync_NewSteamUser_AssignsOnlyPlayerRole()
    {
        await using IdentityTestHost host = await IdentityTestHost.CreateAsync();
        TestSignInManager signInManager = TestSignInManagerFactory.Create(host);
        signInManager.ExternalLoginInfo = CreateSteamLoginInfo();

        var handler = new SteamLoginHandler(signInManager, host.Users);

        bool result = await handler.HandleAsync();

        Assert.True(result);
        ApplicationUser user = Assert.Single(host.Users.Users);
        IList<string> roles = await host.Users.GetRolesAsync(user);

        Assert.Equal($"steam_{SteamId}", user.UserName);
        Assert.Equal(new[] { RoleNames.Player }, roles);
        Assert.Same(user, signInManager.SignedInUser);
    }

    [Fact]
    public async Task HandleAsync_RepeatedSteamLogin_DoesNotCreateAnotherUser()
    {
        await using IdentityTestHost host = await IdentityTestHost.CreateAsync();
        TestSignInManager signInManager = TestSignInManagerFactory.Create(host);
        signInManager.ExternalLoginInfo = CreateSteamLoginInfo();
        var handler = new SteamLoginHandler(signInManager, host.Users);

        Assert.True(await handler.HandleAsync());

        signInManager.ExternalSignInResult = SignInResult.Success;

        Assert.True(await handler.HandleAsync());
        Assert.Equal(1, await host.Users.Users.CountAsync());

        ApplicationUser user = Assert.Single(host.Users.Users);
        IList<UserLoginInfo> logins = await host.Users.GetLoginsAsync(user);
        UserLoginInfo login = Assert.Single(logins);
        Assert.Equal("Steam", login.LoginProvider);
        Assert.Equal(SteamProviderKey, login.ProviderKey);
    }

    private static ExternalLoginInfo CreateSteamLoginInfo() =>
        new(
            new ClaimsPrincipal(new ClaimsIdentity()),
            "Steam",
            SteamProviderKey,
            "Steam");
}
