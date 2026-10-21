namespace RustServer.Plugins.Features.PluginManagement.Operations;

public sealed class OperationHistoryException(string message, Exception? innerException = null)
    : Exception(message, innerException);
