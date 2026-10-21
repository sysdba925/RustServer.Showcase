namespace RustServer.Updater.Api.Applications;

/// <summary>
/// Represents the result of a systemd service activity check.
/// </summary>
public enum SystemdServiceActivity
{
    Active,
    Inactive,
    Unknown
}
