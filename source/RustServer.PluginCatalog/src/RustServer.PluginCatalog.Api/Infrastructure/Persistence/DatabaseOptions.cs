namespace RustServer.PluginCatalog.Api.Infrastructure.Persistence;

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    public string? Provider { get; init; }
}
