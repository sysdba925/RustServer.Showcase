using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using RustServer.Auth.Features.Authorization;
using RustServer.Auth.Infrastructure.Identity;
using RustServer.Auth.Tests.Infrastructure.Testing;

namespace RustServer.Auth.Tests.Integration;

public sealed class AuthenticationVerificationTests
{
    private const string Password = "VerificationPassword!123";

    [Fact]
    public async Task Verify_Unauthenticated_ReturnsUnauthorized()
    {
        using var factory = new AuthWebApplicationFactory();
        using HttpClient client = CreateClient(factory);

        HttpResponseMessage response = await client.GetAsync("/api/auth/verify");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Verify_Authenticated_ReturnsOk()
    {
        using var factory = new AuthWebApplicationFactory();
        using HttpClient client = CreateClient(factory);
        await factory.CreateUserAsync("verified-user", Password);

        await SignInAsync(client, "verified-user");

        HttpResponseMessage response = await client.GetAsync("/api/auth/verify");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task AuthorizeAdmin_Unauthenticated_ReturnsUnauthorized()
    {
        using var factory = new AuthWebApplicationFactory();
        using HttpClient client = CreateClient(factory);

        HttpResponseMessage response = await client.GetAsync("/api/auth/authorize/admin");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task AuthorizeAdmin_AuthenticatedNonAdmin_ReturnsForbidden()
    {
        using var factory = new AuthWebApplicationFactory();
        using HttpClient client = CreateClient(factory);
        await factory.CreateUserAsync("non-admin-user", Password);

        await SignInAsync(client, "non-admin-user");

        HttpResponseMessage response = await client.GetAsync("/api/auth/authorize/admin");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task AuthorizeAdmin_AuthenticatedAdmin_ReturnsOk()
    {
        using var factory = new AuthWebApplicationFactory();
        using HttpClient client = CreateClient(factory);
        ApplicationUser admin = await factory.CreateUserAsync("admin-user", Password);

        using (IServiceScope scope = factory.Services.CreateScope())
        {
            UserManager<ApplicationUser> users =
                scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            ApplicationUser storedAdmin = await users.FindByIdAsync(admin.Id)
                ?? throw new InvalidOperationException("Admin user was not found.");
            IdentityResult result = await users.AddToRoleAsync(storedAdmin, RoleNames.Admin);
            Assert.True(result.Succeeded);
        }

        await SignInAsync(client, "admin-user");

        HttpResponseMessage response = await client.GetAsync("/api/auth/authorize/admin");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());
    }

    private static HttpClient CreateClient(AuthWebApplicationFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

    private static async Task SignInAsync(HttpClient client, string userName)
    {
        HttpResponseMessage csrfResponse = await client.GetAsync("/api/auth/csrf");
        csrfResponse.EnsureSuccessStatusCode();
        CsrfTokenResponse csrf =
            await csrfResponse.Content.ReadFromJsonAsync<CsrfTokenResponse>()
            ?? throw new InvalidOperationException("CSRF token was not returned.");

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new { userName, password = Password })
        };
        request.Headers.Add("X-CSRF-TOKEN", csrf.Token);

        HttpResponseMessage response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private sealed record CsrfTokenResponse(string Token);
}
