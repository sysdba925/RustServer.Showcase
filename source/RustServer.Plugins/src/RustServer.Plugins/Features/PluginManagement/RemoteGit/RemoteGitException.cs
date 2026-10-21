namespace RustServer.Plugins.Features.PluginManagement.RemoteGit;

public sealed class RemoteGitException(string message, Exception? innerException = null)
    : Exception(message, innerException);
