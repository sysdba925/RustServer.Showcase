using Microsoft.AspNetCore.Authentication;

namespace RustServer.Auth.Infrastructure.Authentication;

public static class SteamAuthenticationExtensions
{
    public static AuthenticationBuilder AddSteamProvider(
        this AuthenticationBuilder builder)
    {
        return builder.AddSteam();
    }
}