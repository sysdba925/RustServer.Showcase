namespace RustServer.Plugins.Features.PluginManagement.Guards;

public sealed class ControlOptions
{
    public const string SectionName = "PluginManagement:Control";

    public string BaseUrl { get; init; } = string.Empty;
}
