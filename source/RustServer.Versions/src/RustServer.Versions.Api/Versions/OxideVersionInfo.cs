namespace RustServer.Versions.Api.Versions;

/// <summary>
/// Version state for Oxide.Rust.
/// </summary>
/// <param name="InstalledVersion">Oxide version installed on disk, or null when Oxide is not installed.</param>
/// <param name="RunningVersion">Oxide version reported by the running server, or <see langword="null"/> when it is stopped.</param>
/// <param name="AvailableVersion">Latest Oxide version available from GitHub.</param>
/// <param name="UpdateAvailable">Whether Oxide needs installation or the available version differs from the installed version.</param>
public sealed record OxideVersionInfo(
    string? InstalledVersion,
    string? RunningVersion,
    string AvailableVersion,
    bool UpdateAvailable);
