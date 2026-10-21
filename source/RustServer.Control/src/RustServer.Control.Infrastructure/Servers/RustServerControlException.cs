namespace RustServer.Control.Infrastructure.Servers;

public sealed class RustServerControlException : Exception
{
    public RustServerControlException(string operation, int exitCode)
        : base($"Rust server {operation} operation failed with exit code {exitCode}.")
    {
        Operation = operation;
        ExitCode = exitCode;
    }

    public string Operation { get; }

    public int ExitCode { get; }
}
