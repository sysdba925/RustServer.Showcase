using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace RustServer.Plugins.Infrastructure.OpenApi;

public sealed class ConfigureSwaggerGenOptions(
    IOptions<ApiDocumentationOptions> documentationOptions) : IConfigureOptions<SwaggerGenOptions>
{
    public void Configure(SwaggerGenOptions options)
    {
        var documentation = documentationOptions.Value;

        options.SwaggerDoc(
            ApiDocumentationOptions.DocumentName,
            new OpenApiInfo
            {
                Title = documentation.Title,
                Version = documentation.Version
            });
    }
}
