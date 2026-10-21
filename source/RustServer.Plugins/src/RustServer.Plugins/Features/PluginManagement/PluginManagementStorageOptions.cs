namespace RustServer.Plugins.Features.PluginManagement;

public sealed class PluginManagementStorageOptions
{
    public const string SectionName = "PluginManagement";

    public string StateRootPath { get; set; } = string.Empty;
}
