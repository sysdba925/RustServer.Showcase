namespace RustServer.Control.Infrastructure.Servers;

public sealed class RustServerControlOptions
{
    public const string SectionName = "RustServerControl";

    public string RustServerComposePath { get; init; } = string.Empty;

    /// <summary>
    /// Absolute host directory bind-mounted as <c>/server</c> for the Rust container.
    /// Required only for wipe operations.
    /// </summary>
    public string RustServerDataPath { get; init; } = string.Empty;

    /// <summary>
    /// Infrastructure identity selecting the Rust save directory under <c>server</c>.
    /// Required only for wipe operations and intentionally not part of game-server configuration.
    /// </summary>
    public string RustServerIdentity { get; init; } = string.Empty;

    public TimeSpan GracefulShutdownTimeout { get; init; } = TimeSpan.FromSeconds(30);

    public TimeSpan ShutdownPollInterval { get; init; } = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Limits a WebRCON readiness probe without changing normal command timeouts.
    /// </summary>
    public TimeSpan ReadinessTimeout { get; init; } = TimeSpan.FromSeconds(2);
}
