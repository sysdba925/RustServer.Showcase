using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AspNet.Security.OpenId.Steam;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RustServer.Auth.Features.Authorization;
using RustServer.Auth.Infrastructure.Identity;
using RustServer.Auth.Tests.Infrastructure.Testing;

namespace RustServer.Auth.Tests.Integration;

public sealed class DirectRoutingTests
{
    [Fact]
    public async Task LoginPageAndAssets_AreServedAtTheirDirectRoutes()
    {
        using var factory = new AuthWebApplicationFactory();
        using HttpClient client = CreateClient(factory);

        HttpResponseMessage login = await client.GetAsync("/login");
        HttpResponseMessage css = await client.GetAsync("/login.css");
        HttpResponseMessage javascript = await client.GetAsync("/login.js");

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.Equal("text/html", login.Content.Headers.ContentType?.MediaType);
        string loginHtml = await login.Content.ReadAsStringAsync();
        Assert.Contains("SERVER ACCESS", loginHtml);
        Assert.Contains(
            "<link rel=\"icon\" type=\"image/x-icon\" href=\"https://rust.facepunch.com/favicon.ico\">",
            loginHtml,
            StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, css.StatusCode);
        Assert.Equal(HttpStatusCode.OK, javascript.StatusCode);
    }

    [Fact]
    public async Task AdminPortal_UsesTheSameOfficialRustFavicon()
    {
        using var factory = new AuthWebApplicationFactory();
        using HttpClient client = CreateClient(factory);
        const string userName = "favicon-admin";
        const string password = "FaviconAdminPassword!123";
        ApplicationUser user = await factory.CreateUserAsync(userName, password);

        using (IServiceScope scope = factory.Services.CreateScope())
        {
            UserManager<ApplicationUser> users =
                scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            ApplicationUser storedUser = await users.FindByIdAsync(user.Id)
                ?? throw new InvalidOperationException("Admin user was not found.");
            IdentityResult roleResult = await users.AddToRoleAsync(storedUser, RoleNames.Admin);
            Assert.True(roleResult.Succeeded);
        }

        await SignInAsync(client, userName, password);

        HttpResponseMessage portal = await client.GetAsync("/admin");
        Assert.Equal(HttpStatusCode.OK, portal.StatusCode);
        string html = await portal.Content.ReadAsStringAsync();
        Assert.Contains(
            "<link rel=\"icon\" type=\"image/x-icon\" href=\"https://rust.facepunch.com/favicon.ico\">",
            html,
            StringComparison.Ordinal);
        Assert.Contains("https://rust.facepunch.com/favicon.ico", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Root_DoesNotServeTheLoginPage()
    {
        using var factory = new AuthWebApplicationFactory();
        using HttpClient client = CreateClient(factory);

        HttpResponseMessage response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Portal_AnonymousUser_RedirectsToLogin()
    {
        using var factory = new AuthWebApplicationFactory();
        using HttpClient client = CreateClient(factory, allowAutoRedirect: false);

        HttpResponseMessage response = await client.GetAsync("/portal");

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal("/login", response.Headers.Location?.OriginalString);
    }

    [Theory]
    [InlineData(RoleNames.Admin, "/admin")]
    [InlineData(RoleNames.Player, "/player")]
    public async Task Portal_AuthenticatedUser_RedirectsByRole(
        string role,
        string expectedLocation)
    {
        using var factory = new AuthWebApplicationFactory();
        using HttpClient client = CreateClient(factory, allowAutoRedirect: false);
        const string userName = "portal-user";
        const string password = "PortalPassword!123";
        ApplicationUser user = await factory.CreateUserAsync(userName, password);

        using (IServiceScope scope = factory.Services.CreateScope())
        {
            UserManager<ApplicationUser> users =
                scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            ApplicationUser storedUser = await users.FindByIdAsync(user.Id)
                ?? throw new InvalidOperationException("Portal user was not found.");
            IdentityResult result = await users.AddToRoleAsync(storedUser, role);
            Assert.True(result.Succeeded);
        }

        await SignInAsync(client, userName, password);

        HttpResponseMessage response = await client.GetAsync("/portal");

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal(expectedLocation, response.Headers.Location?.OriginalString);
    }

    [Theory]
    [InlineData("/api/auth/google/login-callback", "/login?googleError=1")]
    [InlineData("/api/auth/steam/login-callback", "/login?steamError=1")]
    public async Task FailedExternalLogin_RedirectsToLoginWithProviderError(
        string callbackPath,
        string expectedLocation)
    {
        using var factory = new AuthWebApplicationFactory();
        using HttpClient client = CreateClient(factory, allowAutoRedirect: false);

        HttpResponseMessage response = await client.GetAsync(callbackPath);

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal(expectedLocation, response.Headers.Location?.OriginalString);
    }

    [Fact]
    public void OAuthMiddlewareCallbackPaths_RemainUnchanged()
    {
        using var factory = new AuthWebApplicationFactory();
        GoogleOptions google = factory.Services
            .GetRequiredService<IOptionsMonitor<GoogleOptions>>()
            .Get(GoogleDefaults.AuthenticationScheme);
        SteamAuthenticationOptions steam = factory.Services
            .GetRequiredService<IOptionsMonitor<SteamAuthenticationOptions>>()
            .Get("Steam");

        Assert.Equal("/signin-google", google.CallbackPath.Value);
        Assert.Equal("/signin-steam", steam.CallbackPath.Value);
    }

    [Fact]
    public async Task OpenApiDocument_DescribesLoginAsHtml()
    {
        using var factory = new AuthWebApplicationFactory(environmentName: "Development");
        using HttpClient client = CreateClient(factory);

        HttpResponseMessage response = await client.GetAsync("/openapi/v1.json");
        response.EnsureSuccessStatusCode();

        using JsonDocument document = JsonDocument.Parse(
            await response.Content.ReadAsStreamAsync());
        JsonElement paths = document.RootElement.GetProperty("paths");
        Assert.True(
            paths.TryGetProperty("/login", out JsonElement loginPath),
            paths.GetRawText());
        JsonElement responseDefinition = loginPath
            .GetProperty("get")
            .GetProperty("responses")
            .GetProperty("200")
            .GetProperty("content");

        Assert.True(responseDefinition.TryGetProperty("text/html", out _));
    }

    [Fact]
    public async Task GoogleChallenge_UsesPublicHttpsUrlFromTrustedProxyHeaders()
    {
        using var factory = new AuthWebApplicationFactory(environmentName: "Production");
        HttpContext response = await factory.Server.SendAsync(context =>
        {
            context.Connection.RemoteIpAddress = IPAddress.Loopback;
            context.Request.Method = HttpMethods.Get;
            context.Request.Path = "/api/auth/google/login";
            context.Request.Scheme = Uri.UriSchemeHttp;
            context.Request.Host = new HostString("internal-auth");
            context.Request.Headers["X-Forwarded-For"] = "203.0.113.1";
            context.Request.Headers["X-Forwarded-Proto"] = "https";
            context.Request.Headers["X-Forwarded-Host"] = "auth.example.test";
        });

        Assert.Equal(StatusCodes.Status302Found, response.Response.StatusCode);
        string location = Uri.UnescapeDataString(
            response.Response.Headers.Location.ToString()
            ?? throw new InvalidOperationException("Google challenge did not redirect."));
        Assert.Contains(
            "redirect_uri=https://auth.example.test/signin-google",
            location,
            StringComparison.Ordinal);

        AuthenticationProperties properties = UnprotectGoogleState(factory, location);
        Assert.Equal(
            "https://auth.example.test/api/auth/google/login-callback",
            properties.RedirectUri);
    }

    [Fact]
    public async Task GoogleChallenge_IgnoresHeadersFromUntrustedProxy()
    {
        using var factory = new AuthWebApplicationFactory(environmentName: "Production");
        HttpContext response = await factory.Server.SendAsync(context =>
        {
            context.Connection.RemoteIpAddress = IPAddress.Parse("192.0.2.10");
            context.Request.Method = HttpMethods.Get;
            context.Request.Path = "/api/auth/google/login";
            context.Request.Scheme = Uri.UriSchemeHttps;
            context.Request.Host = new HostString("internal-auth");
            context.Request.Headers["X-Forwarded-For"] = "203.0.113.1";
            context.Request.Headers["X-Forwarded-Proto"] = "http";
            context.Request.Headers["X-Forwarded-Host"] = "untrusted.example.test";
        });

        Assert.Equal(StatusCodes.Status302Found, response.Response.StatusCode);
        string location = Uri.UnescapeDataString(
            response.Response.Headers.Location.ToString());
        Assert.Contains(
            "redirect_uri=https://internal-auth/signin-google",
            location,
            StringComparison.Ordinal);

        AuthenticationProperties properties = UnprotectGoogleState(factory, location);
        Assert.Equal(
            "https://internal-auth/api/auth/google/login-callback",
            properties.RedirectUri);
    }

    private static HttpClient CreateClient(
        AuthWebApplicationFactory factory,
        bool allowAutoRedirect = true) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = allowAutoRedirect,
            BaseAddress = new Uri("https://localhost")
        });

    private static async Task SignInAsync(
        HttpClient client,
        string userName,
        string password)
    {
        HttpResponseMessage csrfResponse = await client.GetAsync("/api/auth/csrf");
        csrfResponse.EnsureSuccessStatusCode();
        CsrfTokenResponse csrf =
            await csrfResponse.Content.ReadFromJsonAsync<CsrfTokenResponse>()
            ?? throw new InvalidOperationException("CSRF token was not returned.");

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new { userName, password })
        };
        request.Headers.Add("X-CSRF-TOKEN", csrf.Token);

        HttpResponseMessage response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static AuthenticationProperties UnprotectGoogleState(
        AuthWebApplicationFactory factory,
        string location)
    {
        GoogleOptions google = factory.Services
            .GetRequiredService<IOptionsMonitor<GoogleOptions>>()
            .Get(GoogleDefaults.AuthenticationScheme);
        string state = QueryHelpers.ParseQuery(new Uri(location).Query)["state"].ToString();

        return google.StateDataFormat.Unprotect(state)
            ?? throw new InvalidOperationException("Google challenge state could not be read.");
    }

    private sealed record CsrfTokenResponse(string Token);
}
