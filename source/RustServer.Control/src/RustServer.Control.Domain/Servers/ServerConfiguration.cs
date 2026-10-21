namespace RustServer.Control.Domain.Servers;

/// <summary>
/// Game-server settings that Control applies when it creates a Rust server instance.
/// Infrastructure settings such as ports, identity, RCON, and logging are intentionally excluded.
/// </summary>
public sealed record ServerConfiguration(
    string Hostname,
    string Description,
    int MaxPlayers,
    int TickRate,
    int SaveInterval,
    int Seed,
    int WorldSize);
