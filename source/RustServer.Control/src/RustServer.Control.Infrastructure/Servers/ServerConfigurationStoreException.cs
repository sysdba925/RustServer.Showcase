namespace RustServer.Control.Infrastructure.Servers;

/// <summary>
/// Indicates that Control-owned server configuration could not be read or durably stored.
/// </summary>
public sealed class ServerConfigurationStoreException : Exception
{
    public ServerConfigurationStoreException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
