namespace RustServer.Plugins.Infrastructure.OpenApi;

public sealed class ApiDocumentationOptions
{
    public const string SectionName = "ApiDocumentation";
    public const string DocumentName = "openapi";

    public string Title { get; init; } = string.Empty;

    public string Version { get; init; } = string.Empty;
}
