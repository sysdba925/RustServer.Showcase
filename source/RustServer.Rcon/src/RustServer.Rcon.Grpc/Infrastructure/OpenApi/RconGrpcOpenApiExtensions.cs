using Microsoft.OpenApi.Models;

namespace RustServer.Rcon.Grpc.Infrastructure.OpenApi;

internal static class RconGrpcOpenApiExtensions
{
    public static IServiceCollection AddRconGrpcOpenApi(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddEndpointsApiExplorer();
        services.AddGrpcSwagger();
        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("openapi", new OpenApiInfo
            {
                Title = "RustServer RCON gRPC API",
                Version = "v1",
                Description = "Internal gRPC contract for Rust WebRCON operations."
            });
            options.IncludeGrpcXmlComments(Path.Combine(
                AppContext.BaseDirectory,
                "RustServer.Rcon.Grpc.xml"));
        });

        return services;
    }

    public static WebApplication UseRconGrpcOpenApi(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.UseSwagger(options => options.RouteTemplate = "{documentName}.json");
        app.UseSwaggerUI(options =>
            options.SwaggerEndpoint("/openapi.json", "RustServer RCON gRPC API v1"));

        return app;
    }
}
