namespace RustServer.Control.Application.Processes;

public sealed record ProcessExecutionResult(int ExitCode, string StdOut, string StdErr);
