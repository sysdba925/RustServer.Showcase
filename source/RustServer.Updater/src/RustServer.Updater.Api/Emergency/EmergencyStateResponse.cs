using RustServer.Updater.Api.Applications;

namespace RustServer.Updater.Api.Emergency;

/// <summary>State required by the built-in emergency administration page.</summary>
public sealed record EmergencyStateResponse(
    ReleaseChannel Channel,
    IReadOnlyCollection<EmergencyApplicationResponse> Applications);

/// <summary>One registered application's state and channel-filtered release choices.</summary>
public sealed record EmergencyApplicationResponse(
    string Name,
    string? InstalledVersion,
    ApplicationStatus Status,
    IReadOnlyList<string> AvailableVersions,
    string? AvailableVersionsError);
