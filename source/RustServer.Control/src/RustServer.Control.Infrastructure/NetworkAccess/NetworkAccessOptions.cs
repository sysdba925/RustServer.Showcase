namespace RustServer.Control.Infrastructure.NetworkAccess;

/// <summary>
/// Persistent state and local privileged-helper endpoint for NetworkAccess.
/// </summary>
public sealed class NetworkAccessOptions
{
    public const string SectionName = "NetworkAccess";
    public const string DefaultStoragePath = "/var/lib/rustserver/control/network-access.json";
    public const string DefaultHelperSocketPath = "/run/rustserver-network-access/network-access-helper.sock";

    public string StoragePath { get; init; } = DefaultStoragePath;

    public string HelperSocketPath { get; init; } = DefaultHelperSocketPath;
}
