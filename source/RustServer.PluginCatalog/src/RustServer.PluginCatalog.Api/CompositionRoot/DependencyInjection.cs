using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi;
using RustServer.PluginCatalog.Api.Infrastructure.Readiness;

namespace RustServer.PluginCatalog.Api.CompositionRoot;

public static class DependencyInjection
{
    public static IServiceCollection AddApiFoundation(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOpenApi(options => options.AddSchemaTransformer((schema, context, _) =>
        {
            if (context.JsonTypeInfo.Type == typeof(ProblemDetails)
                && schema is OpenApiSchema problemDetailsSchema)
            {
                problemDetailsSchema.Properties ??= new Dictionary<string, IOpenApiSchema>();
                problemDetailsSchema.Properties.TryAdd("code", new OpenApiSchema
                {
                    Type = JsonSchemaType.String,
                    Description = "Stable machine-readable catalog error code."
                });
            }

            return Task.CompletedTask;
        }));
        services.AddProblemDetails();
        services.AddHealthChecks()
            .AddCheck<PluginCatalogReadinessHealthCheck>(
                "plugin_catalog_dependencies",
                tags: ["ready"]);

        return services;
    }
}
