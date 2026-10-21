namespace RustServer.Plugins.Features.PluginManagement.Runtime;

public sealed class RuntimeFilesException(
    RuntimeAvailabilityStatus status,
    string message,
    Exception? innerException = null)
    : Exception(message, innerException)
{
    public RuntimeAvailabilityStatus Status { get; } = status;
}
