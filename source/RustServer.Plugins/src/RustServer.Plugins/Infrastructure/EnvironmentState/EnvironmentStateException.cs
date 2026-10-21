namespace RustServer.Plugins.Infrastructure.EnvironmentState;

public sealed class EnvironmentStateException(string message, Exception? innerException = null)
    : Exception(message, innerException);
