namespace RustServer.Control.Domain.StartupTracking;

public sealed record StartupTrackingSchema(
    int Version,
    IReadOnlyList<StartupMilestoneDefinition> Milestones)
{
    public static StartupTrackingSchema Version1 { get; } = new(
        Version: 1,
        Milestones: Array.AsReadOnly<StartupMilestoneDefinition>(
        [
            new("engine", "Unity Engine"),
            new("mono", "Mono Runtime"),
            new("bootstrap", "Bootstrap"),
            new("scene", "Scene Loading"),
            new("oxide", "Oxide Core"),
            new("steamInitialized", "Steam Server Initialized"),
            new("serverStartup", "Server Startup Complete"),
            new("bootstrapCompleted", "Bootstrap Completed"),
            new("steamConnected", "Steam Server Connected")
        ]));
}
