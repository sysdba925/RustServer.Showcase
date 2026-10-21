namespace RustServer.Updater.Api.Applications;

/// <summary>
/// Contains the most recently discovered state for one registered application.
/// </summary>
public sealed record ApplicationSnapshot(
    string Name,
    string? InstalledVersion,
    string? AvailableVersion,
    ApplicationStatus Status);
