namespace RustServer.Control.Domain.NetworkAccess;

/// <summary>
/// The persisted public reachability mode and UDP maintenance entry point for the Rust server.
/// </summary>
public sealed record NetworkAccessSettings(bool PublicEnabled, int MaintenancePort)
{
    public const int DefaultMaintenancePort = 50_015;
    public const int GamePort = 28_015;
    public const int QueryPort = 28_017;

    public static NetworkAccessSettings Default { get; } = new(true, DefaultMaintenancePort);
}
