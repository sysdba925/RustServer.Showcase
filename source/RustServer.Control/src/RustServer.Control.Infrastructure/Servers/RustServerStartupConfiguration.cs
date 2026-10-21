using System.Globalization;
using RustServer.Control.Domain.Servers;

namespace RustServer.Control.Infrastructure.Servers;

/// <summary>
/// Translates Control's typed game-server configuration to the Compose integration contract.
/// The public API deliberately does not expose these environment variable names.
/// </summary>
internal static class RustServerStartupConfiguration
{
    internal static IReadOnlyDictionary<string, string> ToEnvironmentVariables(ServerConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["RUST_SERVER_HOSTNAME"] = configuration.Hostname,
            ["RUST_SERVER_DESCRIPTION"] = configuration.Description,
            ["RUST_SERVER_MAX_PLAYERS"] = configuration.MaxPlayers.ToString(CultureInfo.InvariantCulture),
            ["RUST_SERVER_TICK_RATE"] = configuration.TickRate.ToString(CultureInfo.InvariantCulture),
            ["RUST_SERVER_SAVE_INTERVAL"] = configuration.SaveInterval.ToString(CultureInfo.InvariantCulture),
            ["RUST_SERVER_SEED"] = configuration.Seed.ToString(CultureInfo.InvariantCulture),
            ["RUST_SERVER_WORLD_SIZE"] = configuration.WorldSize.ToString(CultureInfo.InvariantCulture)
        };
    }
}
