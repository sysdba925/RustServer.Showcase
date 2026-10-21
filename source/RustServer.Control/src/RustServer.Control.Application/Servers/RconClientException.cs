namespace RustServer.Control.Application.Servers;

/// <summary>
/// Represents a failed request to the service that owns Rust WebRCON.
/// </summary>
public sealed class RconClientException : Exception
{
    public RconClientException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
