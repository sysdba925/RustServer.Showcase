using RustServer.Plugins.Features.PluginManagement.Models.Git;
using RustServer.Plugins.Features.PluginManagement.RemoteGit;

namespace RustServer.Plugins.Features.PluginManagement.ReleaseDiscovery;

public sealed class ReleaseDiscoveryException(
    RepositoryId repository,
    RemoteGitDiagnostic? diagnostic = null,
    Exception? innerException = null)
    : Exception("The local remote-tracking release state could not be read.", innerException)
{
    public RepositoryId Repository { get; } = repository;

    public RemoteGitDiagnostic? Diagnostic { get; } = diagnostic;
}
