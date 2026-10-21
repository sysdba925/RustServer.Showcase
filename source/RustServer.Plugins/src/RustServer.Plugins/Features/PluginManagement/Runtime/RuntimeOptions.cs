namespace RustServer.Plugins.Features.PluginManagement.Runtime;

public sealed class RuntimeOptions
{
    public const string SectionName = "PluginManagement:Runtime";

    public string ServerRootPath { get; set; } = string.Empty;
}
