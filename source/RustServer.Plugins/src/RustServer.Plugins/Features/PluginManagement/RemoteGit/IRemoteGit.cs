using RustServer.Plugins.Features.PluginManagement.Models;
using RustServer.Plugins.Features.PluginManagement.Models.Git;

namespace RustServer.Plugins.Features.PluginManagement.RemoteGit;

public interface IRemoteGit
{
    Task<RemoteGitFetchResult> FetchAsync(
        RepositoryId repository,
        CancellationToken cancellationToken);

    Task<RemoteGitRepositoryState> GetStateAsync(
        RepositoryId repository,
        string? trackingBranch,
        CancellationToken cancellationToken);

    Task<RemoteGitReleaseBranchesResult> GetReleaseBranchesAsync(
        RepositoryId repository,
        CancellationToken cancellationToken);

    Task<RemoteGitPushResult> PushAsync(
        RepositoryId repository,
        Source source,
        CancellationToken cancellationToken);
}
