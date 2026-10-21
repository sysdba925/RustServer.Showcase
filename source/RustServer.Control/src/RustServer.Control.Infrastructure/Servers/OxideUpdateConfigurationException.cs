namespace RustServer.Control.Infrastructure.Servers;

public sealed class OxideUpdateConfigurationException : Exception
{
    public OxideUpdateConfigurationException(string message)
        : base(message)
    {
    }
}
