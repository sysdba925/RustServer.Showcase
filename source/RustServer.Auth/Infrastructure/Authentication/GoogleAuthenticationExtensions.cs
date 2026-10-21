using Microsoft.AspNetCore.Authentication;

namespace RustServer.Auth.Infrastructure.Authentication;

public static class GoogleAuthenticationExtensions
{
    public static AuthenticationBuilder AddGoogleProvider(
        this AuthenticationBuilder builder)
    {
        string? clientId = Environment.GetEnvironmentVariable(
            "GOOGLE_CLIENT_ID");

        string? clientSecret = Environment.GetEnvironmentVariable(
            "GOOGLE_CLIENT_SECRET");

        if (string.IsNullOrWhiteSpace(clientId) ||
            string.IsNullOrWhiteSpace(clientSecret))
        {
            throw new InvalidOperationException(
                "Google credentials are not configured.");
        }

        return builder.AddGoogle(options =>
        {
            options.ClientId = clientId;
            options.ClientSecret = clientSecret;

            options.CallbackPath = "/signin-google";
        });
    }
}