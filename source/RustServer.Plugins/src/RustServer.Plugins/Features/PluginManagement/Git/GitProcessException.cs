namespace RustServer.Plugins.Features.PluginManagement.Git;

public sealed class GitProcessException(string message, Exception? innerException = null)
    : Exception(message, innerException);
