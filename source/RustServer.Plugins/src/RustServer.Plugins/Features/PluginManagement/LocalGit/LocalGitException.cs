namespace RustServer.Plugins.Features.PluginManagement.LocalGit;

public sealed class LocalGitException(string message, Exception? innerException = null)
    : Exception(message, innerException);
