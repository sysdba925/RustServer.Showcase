using Microsoft.AspNetCore.Antiforgery;

namespace RustServer.Auth.Features.Antiforgery.GetCsrfToken;

public sealed class GetCsrfTokenHandler
{
    private readonly IAntiforgery _antiforgery;

    public GetCsrfTokenHandler(IAntiforgery antiforgery)
    {
        _antiforgery = antiforgery;
    }

    public string Handle(HttpContext context)
    {
        var tokens = _antiforgery.GetAndStoreTokens(context);

        return tokens.RequestToken
            ?? throw new InvalidOperationException(
                "CSRF token generation failed.");
    }
}