using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RustServer.Auth.Infrastructure.Identity;

namespace RustServer.Auth.Tests.Infrastructure.Testing;

internal static class TestSignInManagerFactory
{
    public static TestSignInManager Create(IdentityTestHost host)
    {
        IServiceProvider services = host.Services;

        return new TestSignInManager(
            host.Users,
            services.GetRequiredService<IHttpContextAccessor>(),
            services.GetRequiredService<IUserClaimsPrincipalFactory<ApplicationUser>>(),
            services.GetRequiredService<IOptions<IdentityOptions>>(),
            services.GetRequiredService<ILogger<SignInManager<ApplicationUser>>>(),
            services.GetRequiredService<IAuthenticationSchemeProvider>(),
            services.GetRequiredService<IUserConfirmation<ApplicationUser>>());
    }
}
