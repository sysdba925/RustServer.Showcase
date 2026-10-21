namespace RustServer.Control.Infrastructure.StartupTracking;

public sealed class StartupTrackingException : Exception
{
    public StartupTrackingException(string operation)
        : base($"Rust server startup tracking {operation} failed.")
    {
        Operation = operation;
    }

    public string Operation { get; }
}
