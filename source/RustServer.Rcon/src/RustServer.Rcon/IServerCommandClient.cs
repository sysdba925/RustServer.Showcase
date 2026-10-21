namespace RustServer.Rcon;

/// <summary>
/// Executes a console command against the running Rust Dedicated Server.
/// </summary>
public interface IServerCommandClient
{
    Task<ServerCommandResult> ExecuteAsync(
        string command,
        CancellationToken cancellationToken = default);
}
