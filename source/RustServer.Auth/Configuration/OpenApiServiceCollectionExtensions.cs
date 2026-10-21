using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace RustServer.Auth.Configuration;

public static class OpenApiServiceCollectionExtensions
{
    private const string CookieSecuritySchemeName = "RustServerAuthCookie";

    public static IServiceCollection AddAuthOpenApi(this IServiceCollection services)
    {
        services.AddOpenApi(options =>
        {
            options.AddDocumentTransformer((document, _, _) =>
            {
                document.Components ??= new OpenApiComponents();
                document.Components.SecuritySchemes ??=
                    new Dictionary<string, IOpenApiSecurityScheme>();

                document.Components.SecuritySchemes[CookieSecuritySchemeName] =
                    new OpenApiSecurityScheme
                    {
                        Type = SecuritySchemeType.ApiKey,
                        In = ParameterLocation.Cookie,
                        Name = AuthenticationCookie.Name,
                        Description = "ASP.NET Core Identity application cookie."
                    };

                return Task.CompletedTask;
            });

            options.AddOperationTransformer((operation, context, _) =>
            {
                var metadata = context.Description.ActionDescriptor.EndpointMetadata;

                if (metadata.OfType<IAllowAnonymous>().Any() ||
                    !metadata.OfType<IAuthorizeData>().Any())
                {
                    return Task.CompletedTask;
                }

                operation.Security ??= [];
                operation.Security.Add(new OpenApiSecurityRequirement
                {
                    [new OpenApiSecuritySchemeReference(
                        CookieSecuritySchemeName,
                        context.Document,
                        null)] = []
                });

                return Task.CompletedTask;
            });
        });

        return services;
    }
}
