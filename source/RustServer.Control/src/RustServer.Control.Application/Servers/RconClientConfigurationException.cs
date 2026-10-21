namespace RustServer.Control.Application.Servers;

/// <summary>
/// Represents an invalid RCON service client configuration.
/// </summary>
public sealed class RconClientConfigurationException : Exception
{
    public RconClientConfigurationException(string message)
        : base(message)
    {
    }
}
