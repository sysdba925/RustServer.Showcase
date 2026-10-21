namespace RustServer.Control.Domain.Servers;

/// <summary>
/// Represents the Docker lifecycle state and Rust server readiness state.
/// </summary>
public sealed record ServerStatus(bool IsRunning, bool IsReady);
