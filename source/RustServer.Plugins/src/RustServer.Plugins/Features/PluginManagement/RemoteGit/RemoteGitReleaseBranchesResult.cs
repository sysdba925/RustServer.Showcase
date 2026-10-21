using RustServer.Plugins.Features.PluginManagement.Models.Git;

namespace RustServer.Plugins.Features.PluginManagement.RemoteGit;

public sealed record RemoteGitReleaseBranchesResult(
    RepositoryId Repository,
    IReadOnlyList<RemoteGitReleaseBranch> Branches,
    RemoteGitDiagnostic Diagnostic)
{
    public bool IsAvailable => Diagnostic == RemoteGitDiagnostic.None;
}
