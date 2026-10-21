using RustServer.Auth.Features.Authentication.Google.Link;
using RustServer.Auth.Tests.Infrastructure.Testing;

namespace RustServer.Auth.Tests.Features.Authentication.Google;

public sealed class GoogleLinkTests
{
    [Fact]
    public async Task CompleteAsync_WithoutAuthenticatedSession_RejectsLinking()
    {
        await using IdentityTestHost host = await IdentityTestHost.CreateAsync();
        TestSignInManager signInManager = TestSignInManagerFactory.Create(host);
        var handler = new LinkGoogleHandler(signInManager, host.Users);

        bool result = await handler.CompleteAsync(ClaimsPrincipalFactory.Anonymous());

        Assert.False(result);
        Assert.Null(signInManager.ExpectedXsrfUserId);
    }
}
