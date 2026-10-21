namespace RustServer.Plugins.Features.PluginManagement.Baseline;

public sealed class BaselineStateException(string message, Exception? innerException = null)
    : Exception(message, innerException);
