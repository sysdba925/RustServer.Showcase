using RustServer.Plugins.Features.PluginManagement.Runtime;

namespace RustServer.Plugins.Features.PluginManagement.RuntimeChanges;

public sealed record RuntimeDiffResult(
    RuntimeDiffStatus Status,
    IReadOnlyList<RuntimeFileChange> Changes);
