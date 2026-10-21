namespace RustServer.Updater.Api.Applications;

/// <summary>Configures the Updater-owned, read-only application registry.</summary>
public sealed class ApplicationRegistryOptions
{
    /// <summary>The configuration section containing the registry settings.</summary>
    public const string SectionName = "ApplicationRegistry";

    /// <summary>Gets or sets the durable runtime registry path.</summary>
    public string StoragePath { get; init; } = "/etc/rustserver/updater/applications.ini";

    /// <summary>Gets the logical registration which uses self-update deployment semantics.</summary>
    public string UpdaterApplicationName { get; init; } = "RustServer.Updater";
}
