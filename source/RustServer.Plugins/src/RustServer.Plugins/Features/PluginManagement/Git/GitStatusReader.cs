using RustServer.Plugins.Features.PluginManagement.LocalGit;
using RustServer.Plugins.Features.PluginManagement.Models.Git;
using RustServer.Plugins.Features.PluginManagement.RemoteGit;

namespace RustServer.Plugins.Features.PluginManagement.Git;

public sealed class GitStatusReader(
    ILocalGit localGit,
    IRemoteGit remoteGit,
    LastFetchState lastFetchState)
{
    public async Task<GitStatusSnapshot> GetAsync(CancellationToken cancellationToken)
    {
        var plugins = await GetRepositoryStatusAsync(RepositoryId.Plugins, cancellationToken);
        var configs = await GetRepositoryStatusAsync(RepositoryId.Configs, cancellationToken);

        return new GitStatusSnapshot(
            ToReadiness(plugins, configs),
            plugins,
            configs,
            lastFetchState.Get());
    }

    private async Task<GitRepositoryStatusSnapshot> GetRepositoryStatusAsync(
        RepositoryId repository,
        CancellationToken cancellationToken)
    {
        try
        {
            var local = await localGit.GetStateAsync(repository, cancellationToken);
            if (!local.IsRepository)
            {
                return new GitRepositoryStatusSnapshot(repository, true, local, null);
            }

            var remote = await remoteGit.GetStateAsync(
                repository,
                local.TrackingBranch,
                cancellationToken);
            return new GitRepositoryStatusSnapshot(repository, true, local, remote);
        }
        catch (LocalGitException)
        {
            return new GitRepositoryStatusSnapshot(repository, false, null, null);
        }
        catch (RemoteGitException)
        {
            return new GitRepositoryStatusSnapshot(repository, false, null, null);
        }
    }

    private static GitReadiness ToReadiness(
        GitRepositoryStatusSnapshot plugins,
        GitRepositoryStatusSnapshot configs)
    {
        var repositories = new[] { plugins, configs };

        if (repositories.Any(repository => !repository.IsAvailable))
        {
            return GitReadiness.Unavailable;
        }

        if (repositories.Any(IsNotConfigured))
        {
            return GitReadiness.NotConfigured;
        }

        return repositories.All(IsReady)
            ? GitReadiness.Ready
            : GitReadiness.Degraded;
    }

    private static bool IsNotConfigured(GitRepositoryStatusSnapshot repository) =>
        repository.Local?.IsRepository is false ||
        repository.Remote?.IsRemoteConfigured is false;

    private static bool IsReady(GitRepositoryStatusSnapshot repository) =>
        repository.Local is
        {
            IsRepository: true,
            IsClean: true,
            Relation: LocalGitRelation.Equal,
            Diagnostic: LocalGitDiagnostic.None
        } &&
        repository.Remote is
        {
            IsRemoteConfigured: true,
            Head: not null,
            Diagnostic: RemoteGitDiagnostic.None
        };
}
