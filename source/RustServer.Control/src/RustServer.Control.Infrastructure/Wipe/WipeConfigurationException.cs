namespace RustServer.Control.Infrastructure.Wipe;

/// <summary>
/// Indicates invalid infrastructure configuration for a destructive wipe operation.
/// </summary>
public sealed class WipeConfigurationException : Exception
{
    public WipeConfigurationException(string message)
        : base(message)
    {
    }
}
