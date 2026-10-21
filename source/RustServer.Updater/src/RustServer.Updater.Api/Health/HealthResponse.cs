namespace RustServer.Updater.Api.Health;

/// <summary>
/// Indicates whether the Updater process is available to receive requests.
/// </summary>
/// <param name="Status">The current process health status.</param>
public sealed record HealthResponse(string Status);
