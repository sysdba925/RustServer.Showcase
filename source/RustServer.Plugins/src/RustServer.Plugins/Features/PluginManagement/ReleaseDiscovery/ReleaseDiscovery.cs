using RustServer.Plugins.Features.PluginManagement.Models;
using RustServer.Plugins.Features.PluginManagement.Models.Git;
using RustServer.Plugins.Features.PluginManagement.RemoteGit;

namespace RustServer.Plugins.Features.PluginManagement.ReleaseDiscovery;

public sealed class ReleaseDiscoveryReader(IRemoteGit remoteGit)
{
    public async Task<IReadOnlyList<ReleaseDiscoveryEntry>> GetAsync(CancellationToken cancellationToken)
    {
        var plugins = await GetBranchesAsync(RepositoryId.Plugins, cancellationToken);
        var configs = await GetBranchesAsync(RepositoryId.Configs, cancellationToken);

        var pluginBranches = plugins.ToDictionary(item => item.Branch, StringComparer.Ordinal);
        var configBranches = configs.ToDictionary(item => item.Branch, StringComparer.Ordinal);

        return pluginBranches.Keys
            .Concat(configBranches.Keys)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(branch => branch, StringComparer.Ordinal)
            .Select(branch => CreateEntry(
                branch,
                pluginBranches.GetValueOrDefault(branch),
                configBranches.GetValueOrDefault(branch)))
            .ToArray();
    }

    private async Task<IReadOnlyList<RemoteGitReleaseBranch>> GetBranchesAsync(
        RepositoryId repository,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await remoteGit.GetReleaseBranchesAsync(repository, cancellationToken);
            if (!result.IsAvailable)
            {
                throw new ReleaseDiscoveryException(repository, result.Diagnostic);
            }

            return result.Branches;
        }
        catch (RemoteGitException exception)
        {
            throw new ReleaseDiscoveryException(repository, innerException: exception);
        }
    }

    private static ReleaseDiscoveryEntry CreateEntry(
        string branch,
        RemoteGitReleaseBranch? plugins,
        RemoteGitReleaseBranch? configs)
    {
        var version = branch[Release.BranchPrefix.Length..];
        var release = new Release(version);
        var availability = plugins is not null && configs is not null
            ? ReleaseAvailability.Complete
            : ReleaseAvailability.Incomplete;

        return new ReleaseDiscoveryEntry(release, availability, plugins?.Head, configs?.Head);
    }
}
