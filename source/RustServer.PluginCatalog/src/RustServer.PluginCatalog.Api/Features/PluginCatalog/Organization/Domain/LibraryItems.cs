namespace RustServer.PluginCatalog.Api.Features.PluginCatalog.Organization.Domain;

public sealed class Tag
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string Color { get; set; } = "";
}

public sealed class Folder
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
}
