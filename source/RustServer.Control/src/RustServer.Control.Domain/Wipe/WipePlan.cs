namespace RustServer.Control.Domain.Wipe;

/// <summary>
/// Explicit destructive policy for one wipe operation.
/// World and blueprints are independent Rust targets; plugin data targets are semantic names
/// registered by Control, never client-supplied filesystem paths.
/// </summary>
public sealed record WipePlan(
    WipeAction World,
    WipeAction Blueprints,
    IReadOnlyList<PluginDataWipeAction> PluginData);

/// <summary>
/// Requested handling for one registered plugin-data target.
/// </summary>
public sealed record PluginDataWipeAction(string Target, WipeAction Action);
