namespace RustServer.Rcon;

public sealed class RustServerRconException : Exception
{
    public RustServerRconException(string message)
        : base(message)
    {
    }
}
