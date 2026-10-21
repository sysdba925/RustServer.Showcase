using RustServer.Plugins.Features.PluginManagement.Baseline;
using RustServer.Plugins.Features.PluginManagement.LocalGit;
using RustServer.Plugins.Features.PluginManagement.Models.Git;
using RustServer.Plugins.Features.PluginManagement.Runtime;
using BaselineModel = RustServer.Plugins.Features.PluginManagement.Models.Baseline;

namespace RustServer.Plugins.Features.PluginManagement.RuntimeChanges;

public sealed class RuntimeDiffReader(
    IBaselineStore baselineStore,
    ILocalGit localGit,
    IRuntimeFiles runtimeFiles,
    ManagedRuntimeFileReader managedRuntimeFiles,
    ManagedFileMapping mapping)
{
    public async Task<RuntimeDiffResult> GetAsync(CancellationToken cancellationToken)
    {
        var baseline = await baselineStore.GetAsync(cancellationToken);
        if (baseline is null)
        {
            return new RuntimeDiffResult(RuntimeDiffStatus.NotInitialized, []);
        }

        var baselineFiles = await GetBaselineFilesAsync(baseline, cancellationToken);
        IReadOnlyList<ManagedFile> currentRuntimeFiles;

        try
        {
            currentRuntimeFiles = await managedRuntimeFiles.ListAsync(cancellationToken);
        }
        catch (RuntimeFilesException exception)
        {
            throw new RuntimeDiffException(
                RuntimeDiffFailure.RuntimeUnavailable,
                runtimeStatus: exception.Status,
                innerException: exception);
        }

        var baselineByPath = baselineFiles.ToDictionary(file => file.RuntimeRelativePath, StringComparer.Ordinal);
        var runtimeByPath = currentRuntimeFiles.ToDictionary(file => file.RuntimeRelativePath, StringComparer.Ordinal);
        var changes = new List<RuntimeFileChange>();

        foreach (var path in baselineByPath.Keys.Concat(runtimeByPath.Keys).Distinct(StringComparer.Ordinal).OrderBy(path => path, StringComparer.Ordinal))
        {
            var baselineFile = baselineByPath.GetValueOrDefault(path);
            var runtimeFile = runtimeByPath.GetValueOrDefault(path);

            if (baselineFile is null)
            {
                changes.Add(new RuntimeFileChange(runtimeFile!, RuntimeChangeKind.Added));
                continue;
            }

            if (runtimeFile is null)
            {
                changes.Add(new RuntimeFileChange(baselineFile, RuntimeChangeKind.Deleted));
                continue;
            }

            if (!await HasSameContentAsync(baseline, baselineFile, runtimeFile, cancellationToken))
            {
                changes.Add(new RuntimeFileChange(runtimeFile, RuntimeChangeKind.Modified));
            }
        }

        return new RuntimeDiffResult(RuntimeDiffStatus.Ready, changes);
    }

    private async Task<IReadOnlyList<ManagedFile>> GetBaselineFilesAsync(
        BaselineModel baseline,
        CancellationToken cancellationToken)
    {
        var plugins = await GetBaselineFilesAsync(
            RepositoryId.Plugins,
            baseline.PluginsCommit,
            cancellationToken);
        var configs = await GetBaselineFilesAsync(
            RepositoryId.Configs,
            baseline.ConfigsCommit,
            cancellationToken);

        return plugins.Concat(configs).ToArray();
    }

    private async Task<IReadOnlyList<ManagedFile>> GetBaselineFilesAsync(
        RepositoryId repository,
        GitCommitSha commit,
        CancellationToken cancellationToken)
    {
        try
        {
            var paths = await localGit.ListFilesAtCommitAsync(repository, commit, cancellationToken);

            return paths
                .Select(path => mapping.FromRepositoryPath(repository, path))
                .OfType<ManagedFile>()
                .ToArray();
        }
        catch (LocalGitException exception)
        {
            throw new RuntimeDiffException(
                RuntimeDiffFailure.BaselineGitObjectUnavailable,
                repository,
                innerException: exception);
        }
    }

    private async Task<bool> HasSameContentAsync(
        BaselineModel baseline,
        ManagedFile baselineFile,
        ManagedFile runtimeFile,
        CancellationToken cancellationToken)
    {
        byte[] baselineContent;
        byte[] runtimeContent;

        try
        {
            baselineContent = await localGit.ReadFileAtCommitAsync(
                baselineFile.Repository,
                GetCommit(baseline, baselineFile.Repository),
                baselineFile.RepositoryRelativePath,
                cancellationToken);
        }
        catch (LocalGitException exception)
        {
            throw new RuntimeDiffException(
                RuntimeDiffFailure.BaselineGitObjectUnavailable,
                baselineFile.Repository,
                innerException: exception);
        }

        try
        {
            runtimeContent = await runtimeFiles.ReadAsync(runtimeFile, cancellationToken);
        }
        catch (RuntimeFilesException exception)
        {
            throw new RuntimeDiffException(
                RuntimeDiffFailure.RuntimeUnavailable,
                runtimeStatus: exception.Status,
                innerException: exception);
        }

        return baselineContent.AsSpan().SequenceEqual(runtimeContent);
    }

    private static GitCommitSha GetCommit(BaselineModel baseline, RepositoryId repository) =>
        repository switch
        {
            RepositoryId.Plugins => baseline.PluginsCommit,
            RepositoryId.Configs => baseline.ConfigsCommit,
            _ => throw new ArgumentOutOfRangeException(nameof(repository), repository, "Unsupported repository.")
        };
}
