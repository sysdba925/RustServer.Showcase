namespace RustServer.Control.Application.Servers;

/// <summary>
/// Performs Rust console operations through the service that owns the WebRCON connection.
/// </summary>
public interface IRconClient
{
    /// <summary>
    /// Executes a Rust console command.
    /// </summary>
    Task<RconCommandResult> ExecuteCommandAsync(
        string command,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current Rust WebRCON connection state.
    /// </summary>
    Task<RconConnectionState> GetConnectionStatusAsync(
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The response returned by a Rust console command.
/// </summary>
/// <param name="ResponseMessage">The Rust console response text.</param>
public sealed record RconCommandResult(string ResponseMessage);

/// <summary>
/// The Rust WebRCON connection state reported by the RCON service.
/// </summary>
public enum RconConnectionState
{
    Disconnected,
    Connected
}
