namespace RustServer.Control.Infrastructure.Servers;

public sealed class RustServerControlConfigurationException : Exception
{
    public RustServerControlConfigurationException(string message)
        : base(message)
    {
    }
}
