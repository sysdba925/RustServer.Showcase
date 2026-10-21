using RustServer.Plugins.Features.PluginManagement.Models.Git;

namespace RustServer.Plugins.Features.PluginManagement.RemoteGit;

public sealed record RemoteGitPushResult(
    RepositoryId Repository,
    RemoteGitPushStatus Status)
{
    public bool Succeeded => Status is RemoteGitPushStatus.Succeeded;
}
