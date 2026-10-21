using RustServer.Plugins.Features.PluginManagement.Baseline;
using RustServer.Plugins.Features.PluginManagement.LocalGit;
using RustServer.Plugins.Features.PluginManagement.Models.Git;
using RustServer.Plugins.Features.PluginManagement.Runtime;
using System.Text;
using BaselineModel = RustServer.Plugins.Features.PluginManagement.Models.Baseline;

namespace RustServer.Plugins.Features.PluginManagement.RuntimeChanges;

public sealed class RuntimeDiffContentReader(
    IBaselineStore baselineStore,
    ILocalGit localGit,
    IRuntimeFiles runtimeFiles,
    ManagedFileMapping mapping)
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public async Task<RuntimeDiffContentResult> GetAsync(
        RepositoryId repository,
        string runtimeRelativePath,
        CancellationToken cancellationToken)
    {
        var requestedFile = mapping.FromRuntimePath(runtimeRelativePath);
        if (requestedFile is null || requestedFile.Repository != repository)
        {
            throw new RuntimeDiffContentException(RuntimeDiffContentFailure.InvalidEntry);
        }

        var baseline = await baselineStore.GetAsync(cancellationToken);
        if (baseline is null)
        {
            return new RuntimeDiffContentResult(RuntimeDiffStatus.NotInitialized, null);
        }

        var baselineFile = await GetBaselineFileAsync(baseline, requestedFile, cancellationToken);
        var runtimeFile = await GetRuntimeFileAsync(requestedFile, cancellationToken);

        var baselineContent = baselineFile is null
            ? null
            : await ReadBaselineContentAsync(baseline, baselineFile, cancellationToken);
        var runtimeContent = runtimeFile is null
            ? null
            : await ReadRuntimeContentAsync(runtimeFile, cancellationToken);

        var change = GetChange(baselineContent, runtimeContent);
        if (change is null)
        {
            throw new RuntimeDiffContentException(RuntimeDiffContentFailure.EntryNotFound);
        }

        return new RuntimeDiffContentResult(
            RuntimeDiffStatus.Ready,
            new RuntimeDiffContent(
                requestedFile.Repository,
                requestedFile.RuntimeRelativePath,
                change.Value,
                ToContentSide(baselineContent),
                ToContentSide(runtimeContent)));
    }

    private async Task<ManagedFile?> GetBaselineFileAsync(
        BaselineModel baseline,
        ManagedFile requestedFile,
        CancellationToken cancellationToken)
    {
        try
        {
            var paths = await localGit.ListFilesAtCommitAsync(
                requestedFile.Repository,
                GetCommit(baseline, requestedFile.Repository),
                cancellationToken);

            return paths
                .Select(path => mapping.FromRepositoryPath(requestedFile.Repository, path))
                .SingleOrDefault(file => file is not null &&
                    string.Equals(file.RuntimeRelativePath, requestedFile.RuntimeRelativePath, StringComparison.Ordinal));
        }
        catch (LocalGitException exception)
        {
            throw new RuntimeDiffException(
                RuntimeDiffFailure.BaselineGitObjectUnavailable,
                requestedFile.Repository,
                innerException: exception);
        }
    }

    private async Task<ManagedFile?> GetRuntimeFileAsync(
        ManagedFile requestedFile,
        CancellationToken cancellationToken)
    {
        try
        {
            var files = await runtimeFiles.ListAsync(cancellationToken);
            return files.SingleOrDefault(file =>
                file.Repository == requestedFile.Repository &&
                string.Equals(file.RuntimeRelativePath, requestedFile.RuntimeRelativePath, StringComparison.Ordinal));
        }
        catch (RuntimeFilesException exception)
        {
            throw new RuntimeDiffException(
                RuntimeDiffFailure.RuntimeUnavailable,
                runtimeStatus: exception.Status,
                innerException: exception);
        }
    }

    private async Task<byte[]> ReadBaselineContentAsync(
        BaselineModel baseline,
        ManagedFile baselineFile,
        CancellationToken cancellationToken)
    {
        try
        {
            return await localGit.ReadFileAtCommitAsync(
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
    }

    private async Task<byte[]?> ReadRuntimeContentAsync(ManagedFile runtimeFile, CancellationToken cancellationToken)
    {
        try
        {
            return await runtimeFiles.ReadAsync(runtimeFile, cancellationToken);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
        catch (RuntimeFilesException exception)
        {
            throw new RuntimeDiffException(
                RuntimeDiffFailure.RuntimeUnavailable,
                runtimeStatus: exception.Status,
                innerException: exception);
        }
    }

    private static RuntimeChangeKind? GetChange(byte[]? baselineContent, byte[]? runtimeContent)
    {
        if (baselineContent is null)
        {
            return runtimeContent is null ? null : RuntimeChangeKind.Added;
        }

        if (runtimeContent is null)
        {
            return RuntimeChangeKind.Deleted;
        }

        return baselineContent.AsSpan().SequenceEqual(runtimeContent)
            ? null
            : RuntimeChangeKind.Modified;
    }

    private static RuntimeDiffContentSide ToContentSide(byte[]? content)
    {
        if (content is null)
        {
            return new RuntimeDiffContentSide(RuntimeDiffContentSideKind.Missing, null);
        }

        if (content.Contains((byte) 0))
        {
            return new RuntimeDiffContentSide(RuntimeDiffContentSideKind.Binary, null);
        }

        try
        {
            return new RuntimeDiffContentSide(RuntimeDiffContentSideKind.Text, StrictUtf8.GetString(content));
        }
        catch (DecoderFallbackException)
        {
            return new RuntimeDiffContentSide(RuntimeDiffContentSideKind.Binary, null);
        }
    }

    private static GitCommitSha GetCommit(BaselineModel baseline, RepositoryId repository) =>
        repository switch
        {
            RepositoryId.Plugins => baseline.PluginsCommit,
            RepositoryId.Configs => baseline.ConfigsCommit,
            _ => throw new ArgumentOutOfRangeException(nameof(repository), repository, "Unsupported repository.")
        };
}

public sealed record RuntimeDiffContentResult(RuntimeDiffStatus Status, RuntimeDiffContent? Content);

public sealed record RuntimeDiffContent(
    RepositoryId Repository,
    string RuntimeRelativePath,
    RuntimeChangeKind Change,
    RuntimeDiffContentSide Baseline,
    RuntimeDiffContentSide Runtime);

public sealed record RuntimeDiffContentSide(RuntimeDiffContentSideKind Kind, string? Text);

public enum RuntimeDiffContentSideKind
{
    Missing,
    Text,
    Binary
}

public enum RuntimeDiffContentFailure
{
    InvalidEntry,
    EntryNotFound
}

public sealed class RuntimeDiffContentException(RuntimeDiffContentFailure failure)
    : Exception("Runtime Diff content entry could not be read.")
{
    public RuntimeDiffContentFailure Failure { get; } = failure;
}
