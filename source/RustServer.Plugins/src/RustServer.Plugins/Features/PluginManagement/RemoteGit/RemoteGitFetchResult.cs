using RustServer.Plugins.Features.PluginManagement.Models.Git;

namespace RustServer.Plugins.Features.PluginManagement.RemoteGit;

public sealed record RemoteGitFetchResult(
    RepositoryId Repository,
    RemoteGitFetchStatus Status)
{
    public bool Succeeded => Status is RemoteGitFetchStatus.Succeeded;
}
