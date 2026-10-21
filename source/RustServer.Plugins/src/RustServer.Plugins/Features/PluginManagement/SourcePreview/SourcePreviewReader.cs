using RustServer.Plugins.Features.PluginManagement.Baseline;
using RustServer.Plugins.Features.PluginManagement.LocalGit;
using RustServer.Plugins.Features.PluginManagement.Models;
using RustServer.Plugins.Features.PluginManagement.Models.Git;
using RustServer.Plugins.Features.PluginManagement.Runtime;
using RustServer.Plugins.Features.PluginManagement.RuntimeChanges;
using RustServer.Plugins.Infrastructure.EnvironmentState;

namespace RustServer.Plugins.Features.PluginManagement.SourcePreview;

public sealed class SourcePreviewReader(
    IEnvironmentStore environmentStore,
    IBaselineStore baselineStore,
    ILocalGit localGit,
    IRuntimeFiles runtimeFiles,
    ManagedRuntimeFileReader managedRuntimeFiles,
    ManagedFileMapping mapping)
{
    public async Task<SourcePreviewResult> GetAsync(
        Source targetSource,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(targetSource);

        var environment = await environmentStore.GetAsync(cancellationToken);
        if (!EnvironmentSourcePolicy.IsAllowed(environment, targetSource))
        {
            throw new SourcePreviewException(
                SourcePreviewFailure.SourceNotAllowed,
                "The target Source is not allowed in the current Environment.");
        }

        var baseline = await baselineStore.GetAsync(cancellationToken);
        var currentSource = await GetCurrentSourceAsync(cancellationToken);
        var (pluginsHead, configsHead) = await ResolveTargetHeadsAsync(targetSource, cancellationToken);
        var targetFiles = await GetTargetFilesAsync(pluginsHead, configsHead, cancellationToken);
        var changes = await CompareWithRuntimeAsync(targetFiles, pluginsHead, configsHead, cancellationToken);

        return new SourcePreviewResult(
            environment,
            currentSource.Source,
            currentSource.Diagnostic,
            baseline,
            targetSource,
            pluginsHead,
            configsHead,
            changes);
    }

    private async Task<(Source? Source, CurrentSourceDiagnostic? Diagnostic)> GetCurrentSourceAsync(
        CancellationToken cancellationToken)
    {
        LocalGitRepositoryState plugins;
        LocalGitRepositoryState configs;

        try
        {
            plugins = await localGit.GetStateAsync(RepositoryId.Plugins, cancellationToken);
            configs = await localGit.GetStateAsync(RepositoryId.Configs, cancellationToken);
        }
        catch (LocalGitException exception)
        {
            throw new SourcePreviewException(
                SourcePreviewFailure.GitObjectUnavailable,
                "The current local Git Source could not be read.",
                innerException: exception);
        }

        if (string.Equals(plugins.Branch, Source.DevelopmentBranchName, StringComparison.Ordinal) &&
            string.Equals(configs.Branch, Source.DevelopmentBranchName, StringComparison.Ordinal))
        {
            return (Source.Development, null);
        }

        if (TryGetMatchingRelease(plugins.Branch, configs.Branch, out var release))
        {
            return (Source.ForRelease(release), null);
        }

        return (null, new CurrentSourceDiagnostic(plugins.Branch, configs.Branch));
    }

    private async Task<(GitCommitSha Plugins, GitCommitSha Configs)> ResolveTargetHeadsAsync(
        Source targetSource,
        CancellationToken cancellationToken)
    {
        GitCommitSha? pluginsHead;
        GitCommitSha? configsHead;

        try
        {
            pluginsHead = await localGit.GetKnownRemoteSourceHeadAsync(
                RepositoryId.Plugins,
                targetSource,
                cancellationToken);
            configsHead = await localGit.GetKnownRemoteSourceHeadAsync(
                RepositoryId.Configs,
                targetSource,
                cancellationToken);
        }
        catch (LocalGitException exception)
        {
            throw new SourcePreviewException(
                SourcePreviewFailure.GitObjectUnavailable,
                "The known remote Git Source could not be read.",
                innerException: exception);
        }

        var missingRepositories = new[]
            {
                (Repository: RepositoryId.Plugins, Head: pluginsHead),
                (Repository: RepositoryId.Configs, Head: configsHead)
            }
            .Where(item => item.Head is null)
            .Select(item => item.Repository)
            .ToArray();

        if (missingRepositories.Length > 0)
        {
            var failure = targetSource.Kind is SourceKind.Release && missingRepositories.Length == 1
                ? SourcePreviewFailure.ReleaseIncomplete
                : SourcePreviewFailure.TargetSourceUnavailable;
            throw new SourcePreviewException(
                failure,
                "The target Source is not available in both known remote-tracking repositories.",
                missingRepositories);
        }

        return (pluginsHead!, configsHead!);
    }

    private async Task<IReadOnlyList<ManagedFile>> GetTargetFilesAsync(
        GitCommitSha pluginsHead,
        GitCommitSha configsHead,
        CancellationToken cancellationToken)
    {
        var plugins = await GetTargetFilesAsync(RepositoryId.Plugins, pluginsHead, cancellationToken);
        var configs = await GetTargetFilesAsync(RepositoryId.Configs, configsHead, cancellationToken);
        return plugins.Concat(configs).ToArray();
    }

    private async Task<IReadOnlyList<ManagedFile>> GetTargetFilesAsync(
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
            throw new SourcePreviewException(
                SourcePreviewFailure.GitObjectUnavailable,
                "The target Git tree could not be read.",
                repository: repository,
                innerException: exception);
        }
    }

    private async Task<IReadOnlyList<RuntimeFileChange>> CompareWithRuntimeAsync(
        IReadOnlyList<ManagedFile> targetFiles,
        GitCommitSha pluginsHead,
        GitCommitSha configsHead,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<ManagedFile> runtimeManagedFiles;
        try
        {
            runtimeManagedFiles = await managedRuntimeFiles.ListAsync(cancellationToken);
        }
        catch (RuntimeFilesException exception)
        {
            throw new SourcePreviewException(
                SourcePreviewFailure.RuntimeUnavailable,
                "Managed Runtime files are unavailable.",
                runtimeStatus: exception.Status,
                innerException: exception);
        }

        var targetByPath = targetFiles.ToDictionary(file => file.RuntimeRelativePath, StringComparer.Ordinal);
        var runtimeByPath = runtimeManagedFiles.ToDictionary(file => file.RuntimeRelativePath, StringComparer.Ordinal);
        var changes = new List<RuntimeFileChange>();

        foreach (var path in targetByPath.Keys
                     .Concat(runtimeByPath.Keys)
                     .Distinct(StringComparer.Ordinal)
                     .OrderBy(path => path, StringComparer.Ordinal))
        {
            var targetFile = targetByPath.GetValueOrDefault(path);
            var runtimeFile = runtimeByPath.GetValueOrDefault(path);

            if (runtimeFile is null)
            {
                changes.Add(new RuntimeFileChange(targetFile!, RuntimeChangeKind.Added));
                continue;
            }

            if (targetFile is null)
            {
                changes.Add(new RuntimeFileChange(runtimeFile, RuntimeChangeKind.Deleted));
                continue;
            }

            if (!await HasSameContentAsync(targetFile, runtimeFile, pluginsHead, configsHead, cancellationToken))
            {
                changes.Add(new RuntimeFileChange(targetFile, RuntimeChangeKind.Modified));
            }
        }

        return changes;
    }

    private async Task<bool> HasSameContentAsync(
        ManagedFile targetFile,
        ManagedFile runtimeFile,
        GitCommitSha pluginsHead,
        GitCommitSha configsHead,
        CancellationToken cancellationToken)
    {
        byte[] targetContent;
        byte[] runtimeContent;

        try
        {
            targetContent = await localGit.ReadFileAtCommitAsync(
                targetFile.Repository,
                targetFile.Repository is RepositoryId.Plugins ? pluginsHead : configsHead,
                targetFile.RepositoryRelativePath,
                cancellationToken);
        }
        catch (LocalGitException exception)
        {
            throw new SourcePreviewException(
                SourcePreviewFailure.GitObjectUnavailable,
                "The target Git file could not be read.",
                repository: targetFile.Repository,
                filePath: targetFile.RuntimeRelativePath,
                innerException: exception);
        }

        try
        {
            runtimeContent = await runtimeFiles.ReadAsync(runtimeFile, cancellationToken);
        }
        catch (RuntimeFilesException exception)
        {
            throw new SourcePreviewException(
                SourcePreviewFailure.RuntimeUnavailable,
                "A managed Runtime file could not be read.",
                repository: runtimeFile.Repository,
                filePath: runtimeFile.RuntimeRelativePath,
                runtimeStatus: exception.Status,
                innerException: exception);
        }

        return targetContent.AsSpan().SequenceEqual(runtimeContent);
    }

    private static bool TryGetMatchingRelease(string? pluginsBranch, string? configsBranch, out Release release)
    {
        if (pluginsBranch is not null &&
            string.Equals(pluginsBranch, configsBranch, StringComparison.Ordinal) &&
            pluginsBranch.StartsWith(Release.BranchPrefix, StringComparison.Ordinal) &&
            pluginsBranch.Length > Release.BranchPrefix.Length)
        {
            release = new Release(pluginsBranch[Release.BranchPrefix.Length..]);
            return true;
        }

        release = null!;
        return false;
    }
}
