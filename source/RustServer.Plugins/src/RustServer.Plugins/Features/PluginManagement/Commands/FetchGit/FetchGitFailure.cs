using RustServer.Plugins.Features.PluginManagement.Models.Git;

namespace RustServer.Plugins.Features.PluginManagement.Commands.FetchGit;

public enum FetchGitFailureReason
{
    None,
    RepositoryUnavailable,
    RemoteMissing,
    RemoteFetchFailed,
    OperationCanceled
}

public enum FetchGitRejectionReason
{
    OperationInProgress,
}

public sealed record FetchGitFailure(
    RepositoryId Repository,
    FetchGitFailureReason Reason)
{
    public string Phase => "fetch";

    public string ErrorCode => Reason switch
    {
        FetchGitFailureReason.RepositoryUnavailable => "GitRepositoryUnavailable",
        FetchGitFailureReason.RemoteMissing => "GitRemoteMissing",
        FetchGitFailureReason.RemoteFetchFailed => "GitRemoteFetchFailed",
        FetchGitFailureReason.OperationCanceled => "OperationCanceled",
        _ => throw new ArgumentOutOfRangeException(nameof(Reason), Reason, "Unsupported Fetch failure reason.")
    };

    public string ErrorMessage => Reason switch
    {
        FetchGitFailureReason.RepositoryUnavailable => "The local Git repository is unavailable.",
        FetchGitFailureReason.RemoteMissing => "The canonical Git remote is not configured.",
        FetchGitFailureReason.RemoteFetchFailed => "Fetching the Git remote failed.",
        FetchGitFailureReason.OperationCanceled =>
            "The Fetch Git operation was canceled after it started.",
        _ => throw new ArgumentOutOfRangeException(nameof(Reason), Reason, "Unsupported Fetch failure reason.")
    };
}

public sealed record FetchGitRejection(FetchGitRejectionReason Reason);
