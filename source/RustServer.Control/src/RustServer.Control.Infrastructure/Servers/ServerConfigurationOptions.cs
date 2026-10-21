using RustServer.Control.Domain.Servers;

namespace RustServer.Control.Infrastructure.Servers;

/// <summary>
/// Bootstrap values and durable storage location for Control-owned game-server configuration.
/// </summary>
public sealed class ServerConfigurationOptions
{
    public const string SectionName = "ServerConfiguration";
    public const string DefaultStoragePath = "/var/lib/rustserver/control/server-configuration.json";

    /// <summary>
    /// JSON file used by Control to persist desired and effective configuration. An empty value
    /// uses the canonical persistent Control configuration file.
    /// </summary>
    public string StoragePath { get; init; } = DefaultStoragePath;

    public InitialServerConfigurationOptions InitialDesired { get; init; } = new();

    public ServerConfiguration CreateInitialDesired() => new(
        InitialDesired.Hostname,
        InitialDesired.Description,
        InitialDesired.MaxPlayers,
        InitialDesired.TickRate,
        InitialDesired.SaveInterval,
        InitialDesired.Seed,
        InitialDesired.WorldSize);
}

public sealed class InitialServerConfigurationOptions
{
    public string Hostname { get; init; } = "Rust Server";

    public string Description { get; init; } = string.Empty;

    public int MaxPlayers { get; init; } = 50;

    public int TickRate { get; init; } = 30;

    public int SaveInterval { get; init; } = 600;

    public int Seed { get; init; }

    public int WorldSize { get; init; } = 3500;
}
