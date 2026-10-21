namespace RustServer.PluginCatalog.Api.Features.PluginCatalog.Sites.Domain;

public sealed class Site
{
    public Guid Id { get; set; }
    public string Name { get; set; } = null!;
    public string? BaseUrl { get; set; }
}
