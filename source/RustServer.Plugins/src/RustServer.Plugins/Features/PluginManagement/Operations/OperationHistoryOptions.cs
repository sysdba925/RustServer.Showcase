namespace RustServer.Plugins.Features.PluginManagement.Operations;

public sealed class OperationHistoryOptions
{
    public const string SectionName = "PluginManagement:OperationHistory";

    public string DatabasePath { get; set; } = string.Empty;
}
