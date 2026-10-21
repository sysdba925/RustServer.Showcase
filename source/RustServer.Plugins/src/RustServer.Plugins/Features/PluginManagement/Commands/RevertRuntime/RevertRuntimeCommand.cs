using RustServer.Plugins.Features.PluginManagement.Runtime;

namespace RustServer.Plugins.Features.PluginManagement.Commands.RevertRuntime;

public sealed record RevertRuntimeCommand(IReadOnlyList<RevertRuntimeSelection>? Selections)
{
    public static RevertRuntimeCommand All { get; } = new(Selections: null);
}

public sealed record RevertRuntimeSelection(ManagedFile File);
