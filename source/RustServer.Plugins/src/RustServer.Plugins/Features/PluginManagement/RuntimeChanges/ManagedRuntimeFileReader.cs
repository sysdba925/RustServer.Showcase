using RustServer.Plugins.Features.PluginManagement.LocalGit;
using RustServer.Plugins.Features.PluginManagement.Models.Git;
using RustServer.Plugins.Features.PluginManagement.Runtime;

namespace RustServer.Plugins.Features.PluginManagement.RuntimeChanges;

/// <summary>
/// Returns only managed Runtime files according to the local Git workspace ignore rules.
/// Keep this filter shared by Runtime Diff, Preview, Apply and Revert; otherwise an
/// ignored server-owned data file could be committed or accidentally deleted.
/// </summary>
public sealed class ManagedRuntimeFileReader(
    IRuntimeFiles runtimeFiles,
    ILocalGit localGit)
{
    public async Task<IReadOnlyList<ManagedFile>> ListAsync(CancellationToken cancellationToken)
    {
        var candidates = await runtimeFiles.ListAsync(cancellationToken);
        if (candidates.Count == 0)
        {
            return candidates;
        }

        var ignoredByRepository = new Dictionary<RepositoryId, IReadOnlySet<string>>();
        foreach (var group in candidates.GroupBy(file => file.Repository))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                ignoredByRepository[group.Key] = await localGit.GetIgnoredPathsAsync(
                    group.Key,
                    group.Select(file => file.RepositoryRelativePath).Distinct(StringComparer.Ordinal).ToArray(),
                    cancellationToken);
            }
            catch (LocalGitException exception)
            {
                // Fail closed, never silently treat all Runtime files as managed on Git failure.
                throw new RuntimeFilesException(
                    RuntimeAvailabilityStatus.UnsafePath,
                    "Git ignore rules could not be validated for managed Runtime files.",
                    exception);
            }
        }

        return candidates
            .Where(file => !ignoredByRepository[file.Repository].Contains(file.RepositoryRelativePath))
            .ToArray();
    }
}
