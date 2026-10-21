using RustServer.Plugins.Features.PluginManagement.Models.Git;

namespace RustServer.Plugins.Features.PluginManagement.Commands.ApplySource;

public enum ApplySourceFailureReason
{
    GitRepositoryUnavailable,
    GitRemoteMissing,
    GitRemoteFetchFailed,
    ReleaseIncomplete,
    TargetSourceUnavailable,
    GitWorkspaceUnsafe,
    SourcePreparationFailed,
    RuntimeApplyFailed,
    BaselinePersistFailed,
    OperationCanceled
}

public sealed record ApplySourceFailure(
    ApplySourceFailureReason Reason,
    string Phase,
    RepositoryId? Repository = null,
    string? FilePath = null,
    IReadOnlyList<RepositoryId>? MissingRepositories = null)
{
    public string ErrorCode => Reason switch
    {
        ApplySourceFailureReason.GitRepositoryUnavailable => "GitRepositoryUnavailable",
        ApplySourceFailureReason.GitRemoteMissing => "GitRemoteMissing",
        ApplySourceFailureReason.GitRemoteFetchFailed => "GitRemoteFetchFailed",
        ApplySourceFailureReason.ReleaseIncomplete => "ReleaseIncomplete",
        ApplySourceFailureReason.TargetSourceUnavailable => "TargetSourceUnavailable",
        ApplySourceFailureReason.GitWorkspaceUnsafe => "GitWorkspaceUnsafe",
        ApplySourceFailureReason.SourcePreparationFailed => "SourcePreparationFailed",
        ApplySourceFailureReason.RuntimeApplyFailed => "RuntimeApplyFailed",
        ApplySourceFailureReason.BaselinePersistFailed => "BaselinePersistFailed",
        ApplySourceFailureReason.OperationCanceled => "OperationCanceled",
        _ => throw new ArgumentOutOfRangeException(nameof(Reason), Reason, "Unsupported Apply Source failure.")
    };

    public string ErrorMessage => Reason switch
    {
        ApplySourceFailureReason.GitRepositoryUnavailable => "A local Git workspace is unavailable.",
        ApplySourceFailureReason.GitRemoteMissing => "The canonical Git remote is missing.",
        ApplySourceFailureReason.GitRemoteFetchFailed => "Fresh Git fetch did not complete.",
        ApplySourceFailureReason.ReleaseIncomplete => "The requested Release is incomplete across Plugins and Configs.",
        ApplySourceFailureReason.TargetSourceUnavailable => "The requested Source is unavailable after fresh Git validation.",
        ApplySourceFailureReason.GitWorkspaceUnsafe => "A local Git workspace is not safe for Source preparation.",
        ApplySourceFailureReason.SourcePreparationFailed => "The target Source workspace could not be prepared safely.",
        ApplySourceFailureReason.RuntimeApplyFailed => "Managed Runtime files could not be applied safely.",
        ApplySourceFailureReason.BaselinePersistFailed => "The new Baseline could not be persisted.",
        ApplySourceFailureReason.OperationCanceled => "The Apply Source operation was canceled after it started.",
        _ => throw new ArgumentOutOfRangeException(nameof(Reason), Reason, "Unsupported Apply Source failure.")
    };
}

public enum ApplySourceRejectionReason
{
    OperationInProgress,
    SourceNotAllowed
}

public sealed record ApplySourceRejection(ApplySourceRejectionReason Reason);
