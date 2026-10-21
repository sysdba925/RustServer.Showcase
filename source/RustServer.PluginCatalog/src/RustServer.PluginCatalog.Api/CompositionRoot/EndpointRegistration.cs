using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using RustServer.PluginCatalog.Api.Features.PluginCatalog;

namespace RustServer.PluginCatalog.Api.CompositionRoot;

public static class EndpointRegistration
{
    public static WebApplication MapApiEndpoints(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
        }

        app.MapHealthChecks("/health", new HealthCheckOptions
        {
            // Liveness intentionally avoids all database and filesystem I/O.
            Predicate = _ => false
        });
        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains("ready", StringComparer.Ordinal)
        });
        app.MapPluginCatalogEndpoints();

        return app;
    }
}
