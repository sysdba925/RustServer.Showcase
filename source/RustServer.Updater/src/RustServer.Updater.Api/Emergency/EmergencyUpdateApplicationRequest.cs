using System.ComponentModel.DataAnnotations;

namespace RustServer.Updater.Api.Emergency;

/// <summary>Exact channel-permitted GitHub Release to prepare and activate from the emergency page.</summary>
public sealed class EmergencyUpdateApplicationRequest
{
    /// <summary>Exact GitHub Release tag selected from the server-provided release list.</summary>
    [Required]
    public string? Version { get; init; }
}
