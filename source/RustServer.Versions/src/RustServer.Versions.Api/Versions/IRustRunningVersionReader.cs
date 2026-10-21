namespace RustServer.Versions.Api.Versions;

/// <summary>
/// Reads version metadata from the currently running Rust Dedicated Server.
/// </summary>
public interface IRustRunningVersionReader
{
    Task<RustRunningVersion?> ReadVersionAsync(CancellationToken cancellationToken);
}
