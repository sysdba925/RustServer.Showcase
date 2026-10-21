using RustServer.Control.Domain.Wipe;

namespace RustServer.Control.Application.Wipe;

/// <summary>
/// A trusted, explicitly registered handler for one semantic plugin-data target.
/// The generic wipe engine does not inspect plugin file formats.
/// </summary>
public interface IPluginDataWipeHandler
{
    string Target { get; }

    Task<WipePluginDataHandlerResult> ExecuteAsync(
        WipeAction action,
        WipePluginDataContext context,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Filesystem location made available to a trusted plugin-data handler.
/// It is always the configured <c>oxide/data</c> directory and is never supplied by an API caller.
/// </summary>
public sealed record WipePluginDataContext(string PluginDataDirectory);

/// <summary>
/// Path-free result supplied by a plugin-data handler.
/// </summary>
public sealed record WipePluginDataHandlerResult(int AffectedItems);
