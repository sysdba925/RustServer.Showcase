namespace RustServer.Versions.Api.Versions;

/// <summary>
/// Executes Rust console commands through the service that owns the WebRCON connection.
/// </summary>
public interface IRconClient
{
    /// <summary>
    /// Executes a Rust console command.
    /// </summary>
    Task<RconCommandResult> ExecuteCommandAsync(
        string command,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The response returned by a Rust console command.
/// </summary>
/// <param name="ResponseMessage">The Rust console response text.</param>
public sealed record RconCommandResult(string ResponseMessage);

/// <summary>
/// The RCON service could not complete an operation.
/// </summary>
public sealed class RconClientException(string message, Exception innerException) : Exception(message, innerException);
