using RustServer.Plugins.Features.PluginManagement.Models;
using RustServer.Plugins.Features.PluginManagement.Models.Git;

namespace RustServer.Plugins.Features.PluginManagement.LocalGit;

public interface ILocalGit
{
    /// <summary>
    /// Determines paths excluded by the current local Git ignore rules.
    /// Git-tracked paths remain managed, matching git check-ignore semantics.
    /// This method never mutates the workspace.
    /// </summary>
    Task<IReadOnlySet<string>> GetIgnoredPathsAsync(
        RepositoryId repository,
        IReadOnlyList<string> repositoryRelativePaths,
        CancellationToken cancellationToken);

    Task<LocalGitRepositoryState> GetStateAsync(
        RepositoryId repository,
        CancellationToken cancellationToken);

    Task<LocalGitDevelopmentUpdateResult> TryFastForwardDevelopmentAsync(
        RepositoryId repository,
        CancellationToken cancellationToken);

    Task FastForwardCurrentBranchAsync(
        RepositoryId repository,
        CancellationToken cancellationToken);

    Task<LocalGitMergeResult> MergeCurrentBranchWithUpstreamAsync(
        RepositoryId repository,
        string authorName,
        string authorEmail,
        CancellationToken cancellationToken);

    Task<bool> IsMergeInProgressAsync(
        RepositoryId repository,
        CancellationToken cancellationToken);

    Task AbortMergeAsync(
        RepositoryId repository,
        LocalGitRepositoryState originalState,
        CancellationToken cancellationToken);

    Task<GitCommitSha?> GetKnownRemoteSourceHeadAsync(
        RepositoryId repository,
        Source source,
        CancellationToken cancellationToken);

    Task<LocalGitSourcePreparation> ValidateSourcePreparationAsync(
        RepositoryId repository,
        Source source,
        GitCommitSha targetHead,
        CancellationToken cancellationToken);

    Task<LocalGitSourcePreparation> PrepareSourceAsync(
        RepositoryId repository,
        Source source,
        GitCommitSha targetHead,
        CancellationToken cancellationToken);

    Task<LocalGitSourcePreparation> PrepareSourceWithoutSynchronizationAsync(
        RepositoryId repository,
        Source source,
        GitCommitSha targetHead,
        CancellationToken cancellationToken);

    Task RestoreSourcePreparationAsync(
        RepositoryId repository,
        LocalGitRepositoryState previousState,
        LocalGitSourcePreparation preparation,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<string>> ListFilesAtCommitAsync(
        RepositoryId repository,
        GitCommitSha commit,
        CancellationToken cancellationToken);

    Task<byte[]> ReadFileAtCommitAsync(
        RepositoryId repository,
        GitCommitSha commit,
        string repositoryRelativePath,
        CancellationToken cancellationToken);

    Task WriteWorkingFileAsync(
        RepositoryId repository,
        string repositoryRelativePath,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken);

    Task RemoveWorkingFileAsync(
        RepositoryId repository,
        string repositoryRelativePath,
        CancellationToken cancellationToken);

    Task StageWorkingFileAsync(
        RepositoryId repository,
        string repositoryRelativePath,
        CancellationToken cancellationToken);

    Task<LocalGitStagingInspection> InspectStagingAsync(
        RepositoryId repository,
        CancellationToken cancellationToken);

    Task<GitCommitSha> CreateCommitAsync(
        RepositoryId repository,
        LocalGitCommit commit,
        CancellationToken cancellationToken);

    Task RestoreOperationChangesAsync(
        RepositoryId repository,
        IReadOnlyList<LocalGitWorkingChange> changes,
        CancellationToken cancellationToken);

    Task RestoreCreatedCommitAsync(
        RepositoryId repository,
        Source source,
        GitCommitSha previousHead,
        GitCommitSha createdCommit,
        CancellationToken cancellationToken);
}
