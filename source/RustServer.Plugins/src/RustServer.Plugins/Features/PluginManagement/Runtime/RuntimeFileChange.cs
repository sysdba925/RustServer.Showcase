namespace RustServer.Plugins.Features.PluginManagement.Runtime;

public sealed record RuntimeFileChange(ManagedFile File, RuntimeChangeKind Change);
