namespace RustServer.Versions.Api.Versions;

/// <summary>
/// Version metadata reported by the currently running Rust Dedicated Server. This metadata is distinct from Steam BuildIDs.
/// </summary>
/// <param name="Protocol">Rust protocol version.</param>
/// <param name="BuildDate">UTC timestamp embedded in the running server build.</param>
/// <param name="UnityVersion">Unity version used to build the server.</param>
/// <param name="Changeset">Rust source changeset number.</param>
/// <param name="Branch">Rust branch reported by the server.</param>
public sealed record RustRunningVersion(
    string Protocol,
    DateTime BuildDate,
    string UnityVersion,
    int Changeset,
    string Branch);
