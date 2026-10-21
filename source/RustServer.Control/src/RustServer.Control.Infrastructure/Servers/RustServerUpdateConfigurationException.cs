namespace RustServer.Control.Infrastructure.Servers;

public sealed class RustServerUpdateConfigurationException : Exception
{
    public RustServerUpdateConfigurationException(string message)
        : base(message)
    {
    }
}
