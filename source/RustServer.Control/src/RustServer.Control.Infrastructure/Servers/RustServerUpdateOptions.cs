namespace RustServer.Control.Infrastructure.Servers;

public sealed class RustServerUpdateOptions
{
    public const string SectionName = "RustServerUpdate";

    public string SteamCmdComposePath { get; init; } = string.Empty;
}
