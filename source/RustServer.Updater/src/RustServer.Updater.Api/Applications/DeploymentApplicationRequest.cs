using System.ComponentModel.DataAnnotations;

namespace RustServer.Updater.Api.Applications;

/// <summary>Explicit verified package version to install or activate.</summary>
public sealed class DeploymentApplicationRequest
{
    /// <summary>Exact release tag that must already have a verified Stage 6 package.</summary>
    [Required]
    public string? Version { get; init; }

    /// <summary>Creates a request for tests and internal callers.</summary>
    public DeploymentApplicationRequest(string? version) => Version = version;
}

/// <summary>Optional explicit release target for a rollback.</summary>
public sealed class RollbackApplicationRequest
{
    /// <summary>Previously installed release to reactivate; omitted selects the previous successful release.</summary>
    public string? Version { get; init; }
}
