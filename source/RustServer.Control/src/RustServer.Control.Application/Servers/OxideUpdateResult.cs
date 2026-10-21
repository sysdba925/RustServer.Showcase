namespace RustServer.Control.Application.Servers;

public sealed record OxideUpdateResult(
    bool Succeeded,
    int ExitCode);
