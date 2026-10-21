using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace RustServer.Updater.Api.Applications;

/// <summary>
/// Describes an application that this updater instance is allowed to inspect and deploy.
/// </summary>
public sealed record ApplicationRegistration
{
    /// <summary>Creates a registration from the durable JSON representation.</summary>
    [JsonConstructor]
    public ApplicationRegistration(
        string name,
        string repository,
        string installationPath,
        ApplicationType type = ApplicationType.DotNet,
        string? serviceName = null,
        string? entrypoint = null,
        string? entryFile = null,
        ApplicationReadinessProbe? readiness = null,
        string? installHook = null)
    {
        Name = name;
        Repository = repository;
        InstallationPath = installationPath;
        Type = type;
        ServiceName = serviceName;
        Entrypoint = entrypoint;
        EntryFile = entryFile;
        Readiness = readiness;
        InstallHook = installHook;
    }

    /// <summary>Creates a backward-compatible .NET registration.</summary>
    public ApplicationRegistration(
        string name,
        string repository,
        string installationPath,
        string serviceName,
        string entrypoint,
        ApplicationReadinessProbe? readiness = null,
        string? installHook = null)
        : this(
            name,
            repository,
            installationPath,
            ApplicationType.DotNet,
            serviceName,
            entrypoint,
            entryFile: null,
            readiness,
            installHook)
    {
    }

    public string Name { get; init; }
    public string Repository { get; init; }
    public string InstallationPath { get; init; }
    public ApplicationType Type { get; init; }
    /// <summary>Systemd unit name for DotNet applications; absent for StaticWeb applications.</summary>
    [AllowNull]
    public string ServiceName { get; init; }
    /// <summary>DLL entrypoint for DotNet applications; absent for StaticWeb applications.</summary>
    [AllowNull]
    public string Entrypoint { get; init; }
    public string? EntryFile { get; init; }
    /// <summary>Optional HTTP readiness check used to prove a deployment is usable.</summary>
    public ApplicationReadinessProbe? Readiness { get; init; }
    /// <summary>Optional audited first-install hook from a verified root-owned release artifact.</summary>
    public string? InstallHook { get; init; }

    /// <summary>Compatibility alias for components which query systemd state.</summary>
    [JsonIgnore]
    public string SystemdServiceName => ServiceName;

    /// <summary>Compatibility alias for components which query GitHub Releases.</summary>
    [JsonIgnore]
    public string ReleaseRepository => Repository;

}
