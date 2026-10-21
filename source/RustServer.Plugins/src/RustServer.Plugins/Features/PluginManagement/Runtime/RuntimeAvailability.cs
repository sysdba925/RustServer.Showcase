namespace RustServer.Plugins.Features.PluginManagement.Runtime;

public enum RuntimeAvailabilityStatus
{
    Available,
    ServerRootMissing,
    ManagedRootMissing,
    UnsafePath
}

public sealed record RuntimeAvailability(RuntimeAvailabilityStatus Status)
{
    public bool IsAvailable => Status == RuntimeAvailabilityStatus.Available;
}
