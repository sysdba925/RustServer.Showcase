using RustServer.Plugins.Features.PluginManagement.Runtime;

namespace RustServer.Plugins.Features.PluginManagement.Commands.CommitRuntime;

public sealed record CommitRuntimeCommand(
    string Message,
    IReadOnlyList<CommitRuntimeSelection> Selections);

public sealed record CommitRuntimeSelection(ManagedFile File);
