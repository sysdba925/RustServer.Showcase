namespace RustServer.Plugins.Features.PluginManagement.Guards;

public sealed class ServerStatusUnavailableException(string message, Exception? innerException = null)
    : Exception(message, innerException);
