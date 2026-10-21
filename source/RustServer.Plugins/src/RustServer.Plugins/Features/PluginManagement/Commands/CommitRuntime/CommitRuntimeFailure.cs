using RustServer.Plugins.Features.PluginManagement.Models.Git;

namespace RustServer.Plugins.Features.PluginManagement.Commands.CommitRuntime;

public enum CommitRuntimeFailureReason
{
    GitRepositoryUnavailable,
    GitRemoteMissing,
    GitRemoteFetchFailed,
    GitSourceChangedSinceBaseline,
    GitSynchronizationRequired,
    GitWorkspaceUnsafe,
    InvalidRuntimeSelection,
    BaselineGitObjectUnavailable,
    RuntimeUnavailable,
    GitCommitFailed,
    GitPushFailed,
    BaselinePersistFailed,
    OperationCanceled
}

public sealed record CommitRuntimeFailure(
    CommitRuntimeFailureReason Reason,
    string Phase,
    RepositoryId? Repository = null,
    string? FilePath = null)
{
    public string ErrorCode => Reason switch
    {
        CommitRuntimeFailureReason.GitRepositoryUnavailable => "GitRepositoryUnavailable",
        CommitRuntimeFailureReason.GitRemoteMissing => "GitRemoteMissing",
        CommitRuntimeFailureReason.GitRemoteFetchFailed => "GitRemoteFetchFailed",
        CommitRuntimeFailureReason.GitSourceChangedSinceBaseline => "GitSourceChangedSinceBaseline",
        CommitRuntimeFailureReason.GitSynchronizationRequired => "GitSynchronizationRequired",
        CommitRuntimeFailureReason.GitWorkspaceUnsafe => "GitWorkspaceUnsafe",
        CommitRuntimeFailureReason.InvalidRuntimeSelection => "InvalidRuntimeSelection",
        CommitRuntimeFailureReason.BaselineGitObjectUnavailable => "BaselineGitObjectUnavailable",
        CommitRuntimeFailureReason.RuntimeUnavailable => "RuntimeUnavailable",
        CommitRuntimeFailureReason.GitCommitFailed => "GitCommitFailed",
        CommitRuntimeFailureReason.GitPushFailed => "GitPushFailed",
        CommitRuntimeFailureReason.BaselinePersistFailed => "BaselinePersistFailed",
        CommitRuntimeFailureReason.OperationCanceled => "OperationCanceled",
        _ => throw new ArgumentOutOfRangeException(nameof(Reason), Reason, "Unsupported Commit Runtime failure.")
    };

    public string ErrorMessage => Reason switch
    {
        CommitRuntimeFailureReason.GitRepositoryUnavailable => "A local Git workspace is unavailable.",
        CommitRuntimeFailureReason.GitRemoteMissing => "The canonical Git remote is missing.",
        CommitRuntimeFailureReason.GitRemoteFetchFailed => "Fresh Git fetch did not complete.",
        CommitRuntimeFailureReason.GitSourceChangedSinceBaseline =>
            "The Baseline Source changed in Git after it was applied to the server.",
        CommitRuntimeFailureReason.GitSynchronizationRequired =>
            "The local Source branch requires Git Sync before Runtime changes can be committed.",
        CommitRuntimeFailureReason.GitWorkspaceUnsafe =>
            "A local Git workspace is not safe for Commit + Push Runtime changes.",
        CommitRuntimeFailureReason.InvalidRuntimeSelection =>
            "Selected Runtime changes are no longer present in the current Runtime Diff.",
        CommitRuntimeFailureReason.BaselineGitObjectUnavailable =>
            "The Baseline Git object is unavailable in the local workspace.",
        CommitRuntimeFailureReason.RuntimeUnavailable => "Managed Runtime files are unavailable.",
        CommitRuntimeFailureReason.GitCommitFailed => "The selected Runtime changes could not be committed.",
        CommitRuntimeFailureReason.GitPushFailed => "The local commit could not be pushed to canonical origin.",
        CommitRuntimeFailureReason.BaselinePersistFailed => "The committed Runtime Baseline could not be persisted.",
        CommitRuntimeFailureReason.OperationCanceled =>
            "The Commit + Push Runtime operation was canceled after it started.",
        _ => throw new ArgumentOutOfRangeException(nameof(Reason), Reason, "Unsupported Commit Runtime failure.")
    };
}

public enum CommitRuntimeRejectionReason
{
    OperationInProgress,
    CommitNotAllowedInProduction,
    BaselineNotInitialized
}

public sealed record CommitRuntimeRejection(CommitRuntimeRejectionReason Reason);
