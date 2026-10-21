using System.ComponentModel.DataAnnotations;

namespace RustServer.Updater.Api.Applications;

/// <summary>Explicit release requested for package preparation.</summary>
public sealed class PrepareApplicationRequest
{
    /// <summary>Exact GitHub Release tag to prepare.</summary>
    [Required]
    public string? Version { get; init; }

    /// <summary>Creates a request for tests and internal callers.</summary>
    public PrepareApplicationRequest(string? version) => Version = version;
}
