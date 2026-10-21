using RustServer.Plugins.Features.PluginManagement.Models.Git;

namespace RustServer.Plugins.Features.PluginManagement.Commands.GitSync;

public enum GitSyncFailureReason
{
    GitRepositoryUnavailable,
    GitRemoteMissing,
    GitRemoteFetchFailed,
    GitWorkspaceUnsafe,
    GitSyncFailed,
    GitRemoteChanged,
    GitMergeConflict,
    GitMergeRecoveryFailed,
    OperationCanceled
}

public sealed record GitSyncFailure(
    GitSyncFailureReason Reason,
    string Phase,
    RepositoryId Repository)
{
    public string ErrorCode => Reason switch
    {
        GitSyncFailureReason.GitRepositoryUnavailable => "GitRepositoryUnavailable",
        GitSyncFailureReason.GitRemoteMissing => "GitRemoteMissing",
        GitSyncFailureReason.GitRemoteFetchFailed => "GitRemoteFetchFailed",
        GitSyncFailureReason.GitWorkspaceUnsafe => "GitSyncWorkspaceUnsafe",
        GitSyncFailureReason.GitSyncFailed => "GitSyncFailed",
        GitSyncFailureReason.GitRemoteChanged => "GitRemoteChanged",
        GitSyncFailureReason.GitMergeConflict => "GitMergeConflict",
        GitSyncFailureReason.GitMergeRecoveryFailed => "GitMergeRecoveryFailed",
        GitSyncFailureReason.OperationCanceled => "OperationCanceled",
        _ => throw new ArgumentOutOfRangeException(nameof(Reason), Reason, "Unsupported Git Sync failure.")
    };

    public string ErrorMessage => Reason switch
    {
        GitSyncFailureReason.GitRepositoryUnavailable => "The local Git repository is unavailable.",
        GitSyncFailureReason.GitRemoteMissing => "The canonical Git remote is missing.",
        GitSyncFailureReason.GitRemoteFetchFailed => "Fresh Git fetch did not complete.",
        GitSyncFailureReason.GitWorkspaceUnsafe =>
            "The current Git branch, upstream or workspace is not safe for synchronization.",
        GitSyncFailureReason.GitSyncFailed =>
            "Git synchronization could not complete without a destructive recovery.",
        GitSyncFailureReason.GitRemoteChanged =>
            "The remote changed before Push completed; fetch and run Git Sync again.",
        GitSyncFailureReason.GitMergeConflict =>
            "Git merge encountered conflicts and was aborted.",
        GitSyncFailureReason.GitMergeRecoveryFailed =>
            "Git merge recovery did not restore the original workspace; manual intervention is required.",
        GitSyncFailureReason.OperationCanceled =>
            "The Git Sync operation was canceled after it started.",
        _ => throw new ArgumentOutOfRangeException(nameof(Reason), Reason, "Unsupported Git Sync failure.")
    };
}

public enum GitSyncRejectionReason
{
    OperationInProgress,
}

public sealed record GitSyncRejection(GitSyncRejectionReason Reason);
