namespace RustServer.Control.Infrastructure.Servers;

/// <summary>
/// The host port that Docker Compose publishes for Rust WebRCON.
/// </summary>
/// <remarks>
/// Control uses this value only to preserve the Docker Compose port mapping. The standalone
/// RCON service owns the WebRCON endpoint and credentials.
/// </remarks>
public sealed record RustServerRconPort
{
    public RustServerRconPort(int value)
    {
        if (value is < 1 or > 65_535)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "The RCON port must be a valid TCP port.");
        }

        Value = value;
    }

    public int Value { get; }
}
