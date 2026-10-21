namespace RustServer.Rcon;

public sealed class RustServerRconConfigurationException : Exception
{
    public RustServerRconConfigurationException(string message)
        : base(message)
    {
    }
}
