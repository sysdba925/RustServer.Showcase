namespace RustServer.Rcon;

public sealed class RustServerRconOptions
{
    public const string SectionName = "RustServerRcon";

    public string Host { get; init; } = string.Empty;

    public int Port { get; init; }

    public string Password { get; init; } = string.Empty;

    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(5);

    public TimeSpan CommandTimeout { get; init; } = TimeSpan.FromSeconds(10);

    public int MaxResponseBytes { get; init; } = 65_536;
}
