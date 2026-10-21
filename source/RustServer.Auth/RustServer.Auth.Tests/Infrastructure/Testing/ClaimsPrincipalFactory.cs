using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using RustServer.Auth.Infrastructure.Identity;

namespace RustServer.Auth.Tests.Infrastructure.Testing;

internal static class ClaimsPrincipalFactory
{
    public static ClaimsPrincipal Authenticated(ApplicationUser user, params string[] roles)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id)
        };

        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));

        return new ClaimsPrincipal(new ClaimsIdentity(
            claims,
            IdentityConstants.ApplicationScheme));
    }

    public static ClaimsPrincipal Anonymous() => new(new ClaimsIdentity());
}
