namespace RustServer.Control.Application.Servers;

public sealed record RustServerUpdateResult(
    bool Succeeded,
    int ExitCode);
