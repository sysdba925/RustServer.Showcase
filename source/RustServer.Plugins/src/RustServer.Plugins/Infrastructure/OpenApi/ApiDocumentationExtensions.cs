using Microsoft.Extensions.Options;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace RustServer.Plugins.Infrastructure.OpenApi;

public static class ApiDocumentationExtensions
{
    public static IServiceCollection AddApiDocumentation(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services
            .AddOptions<ApiDocumentationOptions>()
            .Bind(configuration.GetSection(ApiDocumentationOptions.SectionName))
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.Title),
                $"{ApiDocumentationOptions.SectionName}:{nameof(ApiDocumentationOptions.Title)} must contain text.")
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.Version),
                $"{ApiDocumentationOptions.SectionName}:{nameof(ApiDocumentationOptions.Version)} must contain text.")
            .ValidateOnStart();

        services.AddSwaggerGen(options =>
        {
            options.SupportNonNullableReferenceTypes();
            options.OperationFilter<OperationHistoryPaginationOperationFilter>();
        });
        services.AddTransient<IConfigureOptions<SwaggerGenOptions>, ConfigureSwaggerGenOptions>();

        return services;
    }

    public static IApplicationBuilder UseApiDocumentation(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.UseSwagger(options => options.RouteTemplate = "{documentName}.json");

        var environment = app.ApplicationServices.GetRequiredService<IHostEnvironment>();
        if (environment.IsDevelopment())
        {
            var documentation = app.ApplicationServices
                .GetRequiredService<IOptions<ApiDocumentationOptions>>()
                .Value;

            app.UseSwaggerUI(options =>
                options.SwaggerEndpoint(
                    $"/{ApiDocumentationOptions.DocumentName}.json",
                    $"{documentation.Title} {documentation.Version}"));
        }

        return app;
    }
}
