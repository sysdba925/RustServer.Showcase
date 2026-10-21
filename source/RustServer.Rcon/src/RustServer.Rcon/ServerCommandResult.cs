namespace RustServer.Rcon;

/// <summary>
/// The bounded response returned for a server console command.
/// Transport failures are reported by the command client as exceptions.
/// </summary>
public sealed record ServerCommandResult(
    string ResponseMessage);
