using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using RustServer.Auth.Tests.Infrastructure.Testing;

namespace RustServer.Auth.Tests.Integration;

public sealed class AuthenticationFlowTests
{
    private const string UserName = "csrf-user";
    private const string Password = "CsrfPassword!123";

    [Fact]
    public async Task LoginAndLogout_WithoutCsrfToken_ReturnBadRequest()
    {
        using var factory = new AuthWebApplicationFactory();
        using HttpClient client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        await factory.CreateUserAsync(UserName, Password);

        HttpResponseMessage loginWithoutToken = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { userName = UserName, password = Password });

        Assert.Equal(HttpStatusCode.BadRequest, loginWithoutToken.StatusCode);

        HttpResponseMessage csrfResponse = await client.GetAsync("/api/auth/csrf");
        csrfResponse.EnsureSuccessStatusCode();
        CsrfTokenResponse csrf =
            await csrfResponse.Content.ReadFromJsonAsync<CsrfTokenResponse>()
            ?? throw new InvalidOperationException("CSRF token was not returned.");

        using var loginWithToken = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/auth/login")
        {
            Content = JsonContent.Create(new { userName = UserName, password = Password })
        };
        loginWithToken.Headers.Add("X-CSRF-TOKEN", csrf.Token);

        HttpResponseMessage successfulLogin = await client.SendAsync(loginWithToken);
        Assert.Equal(HttpStatusCode.OK, successfulLogin.StatusCode);

        HttpResponseMessage logoutWithoutToken = await client.PostAsync(
            "/api/auth/logout",
            content: null);

        Assert.Equal(HttpStatusCode.BadRequest, logoutWithoutToken.StatusCode);

        HttpResponseMessage csrfAfterLoginResponse = await client.GetAsync("/api/auth/csrf");
        csrfAfterLoginResponse.EnsureSuccessStatusCode();
        CsrfTokenResponse csrfAfterLogin =
            await csrfAfterLoginResponse.Content.ReadFromJsonAsync<CsrfTokenResponse>()
            ?? throw new InvalidOperationException("CSRF token was not returned.");

        using var logoutWithToken = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/auth/logout");
        logoutWithToken.Headers.Add("X-CSRF-TOKEN", csrfAfterLogin.Token);

        HttpResponseMessage successfulLogout = await client.SendAsync(logoutWithToken);
        Assert.Equal(HttpStatusCode.NoContent, successfulLogout.StatusCode);
    }

    private sealed record CsrfTokenResponse(string Token);
}
