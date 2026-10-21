using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using RustServer.Auth.Features.Authentication.Google.Link;
using RustServer.Auth.Features.Authentication.Google.Login;
using RustServer.Auth.Features.Authentication.Steam.Link;
using RustServer.Auth.Features.Authentication.Steam.Login;
using RustServer.Auth.Infrastructure.Identity;
using RustServer.Auth.Tests.Infrastructure.Testing;

namespace RustServer.Auth.Tests.Features.Authentication;

public sealed class ExternalAuthenticationRedirectTests
{
    [Fact]
    public async Task GoogleLoginCallback_Success_RedirectsToPortal()
    {
        await using IdentityTestHost host = await IdentityTestHost.CreateAsync();
        TestSignInManager signInManager = TestSignInManagerFactory.Create(host);
        signInManager.ExternalLoginInfo = CreateExternalLoginInfo("Google", "google-user");
        signInManager.ExternalSignInResult = Microsoft.AspNetCore.Identity.SignInResult.Success;
        var controller = new GoogleLoginController(new GoogleLoginHandler(signInManager));
        ConfigureContext(controller, host, ClaimsPrincipalFactory.Anonymous());

        IActionResult result = await controller.Callback();

        AssertRedirect(result, "/portal");
    }

    [Fact]
    public async Task SteamLoginCallback_Success_RedirectsToPortal()
    {
        await using IdentityTestHost host = await IdentityTestHost.CreateAsync();
        TestSignInManager signInManager = TestSignInManagerFactory.Create(host);
        signInManager.ExternalLoginInfo = CreateExternalLoginInfo("Steam", "steam-user");
        signInManager.ExternalSignInResult = Microsoft.AspNetCore.Identity.SignInResult.Success;
        var controller = new SteamLoginController(
            new SteamLoginHandler(signInManager, host.Users));
        ConfigureContext(controller, host, ClaimsPrincipalFactory.Anonymous());

        IActionResult result = await controller.Callback();

        AssertRedirect(result, "/portal");
    }

    [Theory]
    [InlineData("Google", "/admin?googleLinked=1")]
    [InlineData("Steam", "/admin?steamLinked=1")]
    public async Task ProviderLinkCallback_Success_RedirectsToAdmin(
        string provider,
        string expectedLocation)
    {
        await using IdentityTestHost host = await IdentityTestHost.CreateAsync();
        ApplicationUser user = await host.CreateUserAsync("administrator");
        TestSignInManager signInManager = TestSignInManagerFactory.Create(host);
        signInManager.ExternalLoginInfo = CreateExternalLoginInfo(provider, $"{provider}-user");
        ClaimsPrincipal principal = ClaimsPrincipalFactory.Authenticated(user);

        ControllerBase controller = provider switch
        {
            "Google" => new LinkGoogleController(
                new LinkGoogleHandler(signInManager, host.Users)),
            "Steam" => new LinkSteamController(
                new LinkSteamHandler(signInManager, host.Users)),
            _ => throw new InvalidOperationException($"Unsupported provider '{provider}'.")
        };
        ConfigureContext(controller, host, principal);

        IActionResult result = provider == "Google"
            ? await ((LinkGoogleController)controller).Callback()
            : await ((LinkSteamController)controller).Callback();

        AssertRedirect(result, expectedLocation);
    }

    private static ExternalLoginInfo CreateExternalLoginInfo(
        string provider,
        string providerKey) =>
        new(new ClaimsPrincipal(new ClaimsIdentity()), provider, providerKey, provider);

    private static void ConfigureContext(
        ControllerBase controller,
        IdentityTestHost host,
        ClaimsPrincipal principal)
    {
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                RequestServices = host.Services,
                User = principal
            }
        };
    }

    private static void AssertRedirect(IActionResult result, string expectedLocation)
    {
        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal(expectedLocation, redirect.Url);
    }
}
