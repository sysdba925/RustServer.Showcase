namespace RustServer.Control.Domain.Servers;

/// <summary>
/// The operator's desired configuration and, when the server has been launched by Control,
/// the configuration effective for the current Rust process.
/// </summary>
public sealed record ServerConfigurationState(
    ServerConfiguration Desired,
    ServerConfiguration? Effective);
