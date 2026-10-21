using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RustServer.Auth.Infrastructure.Identity;

namespace RustServer.Auth.Tests.Infrastructure.Testing;

internal sealed class TestSignInManager : SignInManager<ApplicationUser>
{
    public TestSignInManager(
        UserManager<ApplicationUser> userManager,
        IHttpContextAccessor contextAccessor,
        IUserClaimsPrincipalFactory<ApplicationUser> claimsFactory,
        IOptions<IdentityOptions> optionsAccessor,
        ILogger<SignInManager<ApplicationUser>> logger,
        IAuthenticationSchemeProvider schemes,
        IUserConfirmation<ApplicationUser> confirmation)
        : base(
            userManager,
            contextAccessor,
            claimsFactory,
            optionsAccessor,
            logger,
            schemes,
            confirmation)
    {
    }

    public ExternalLoginInfo? ExternalLoginInfo { get; set; }

    public SignInResult ExternalSignInResult { get; set; } = SignInResult.Failed;

    public string? ExpectedXsrfUserId { get; private set; }

    public ApplicationUser? SignedInUser { get; private set; }

    public override Task<ExternalLoginInfo?> GetExternalLoginInfoAsync(
        string? expectedXsrf = null)
    {
        ExpectedXsrfUserId = expectedXsrf;
        return Task.FromResult(ExternalLoginInfo);
    }

    public override Task<SignInResult> ExternalLoginSignInAsync(
        string loginProvider,
        string providerKey,
        bool isPersistent,
        bool bypassTwoFactor)
    {
        return Task.FromResult(ExternalSignInResult);
    }

    public override Task SignInAsync(
        ApplicationUser user,
        bool isPersistent,
        string? authenticationMethod = null)
    {
        SignedInUser = user;
        return Task.CompletedTask;
    }
}
