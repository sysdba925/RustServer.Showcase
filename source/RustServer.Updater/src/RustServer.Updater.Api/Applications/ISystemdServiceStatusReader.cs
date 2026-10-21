namespace RustServer.Updater.Api.Applications;

/// <summary>
/// Executes the fixed systemd status check for a configured service.
/// </summary>
public interface ISystemdServiceStatusReader
{
    /// <summary>
    /// Determines whether a systemd service is active.
    /// </summary>
    /// <param name="serviceName">A configured systemd service name.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    Task<SystemdServiceActivity> GetActivityAsync(string serviceName, CancellationToken cancellationToken);
}
