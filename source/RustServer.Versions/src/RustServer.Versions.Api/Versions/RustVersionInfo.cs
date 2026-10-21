namespace RustServer.Versions.Api.Versions;

/// <summary>
/// Version state for Rust Dedicated Server.
/// </summary>
/// <param name="Branch">Steam branch used by the server installation.</param>
/// <param name="InstalledBuildId">Steam BuildID installed on disk, or null when Rust is not installed.</param>
/// <param name="Running">Version metadata reported by the running server, or <see langword="null"/> when it is stopped.</param>
/// <param name="AvailableBuildId">Latest Steam BuildID available for the configured branch.</param>
/// <param name="UpdateAvailable">Whether Rust needs installation or the available BuildID differs from the installed BuildID.</param>
public sealed record RustVersionInfo(
    string Branch,
    ulong? InstalledBuildId,
    RustRunningVersion? Running,
    ulong AvailableBuildId,
    bool UpdateAvailable);
