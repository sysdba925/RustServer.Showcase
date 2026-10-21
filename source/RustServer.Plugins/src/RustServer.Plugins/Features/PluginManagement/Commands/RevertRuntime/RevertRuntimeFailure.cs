using RustServer.Plugins.Features.PluginManagement.Models.Git;

namespace RustServer.Plugins.Features.PluginManagement.Commands.RevertRuntime;

public enum RevertRuntimeFailureReason
{
    InvalidRuntimeSelection,
    BaselineGitObjectUnavailable,
    RuntimeUnavailable,
    RuntimeRevertFailed,
    OperationCanceled
}

public sealed record RevertRuntimeFailure(
    RevertRuntimeFailureReason Reason,
    string Phase,
    RepositoryId? Repository = null,
    string? FilePath = null)
{
    public string ErrorCode => Reason switch
    {
        RevertRuntimeFailureReason.InvalidRuntimeSelection => "InvalidRuntimeSelection",
        RevertRuntimeFailureReason.BaselineGitObjectUnavailable => "BaselineGitObjectUnavailable",
        RevertRuntimeFailureReason.RuntimeUnavailable => "RuntimeUnavailable",
        RevertRuntimeFailureReason.RuntimeRevertFailed => "RuntimeRevertFailed",
        RevertRuntimeFailureReason.OperationCanceled => "OperationCanceled",
        _ => throw new ArgumentOutOfRangeException(nameof(Reason), Reason, "Unsupported Runtime Revert failure.")
    };

    public string ErrorMessage => Reason switch
    {
        RevertRuntimeFailureReason.InvalidRuntimeSelection =>
            "The selected Runtime changes are invalid or no longer exist in the Runtime Diff.",
        RevertRuntimeFailureReason.BaselineGitObjectUnavailable =>
            "A Baseline Git object is unavailable in the local object database.",
        RevertRuntimeFailureReason.RuntimeUnavailable => "Managed Runtime files are unavailable.",
        RevertRuntimeFailureReason.RuntimeRevertFailed =>
            "Managed Runtime files could not be restored to the Baseline.",
        RevertRuntimeFailureReason.OperationCanceled =>
            "The Runtime Revert operation was canceled after it started.",
        _ => throw new ArgumentOutOfRangeException(nameof(Reason), Reason, "Unsupported Runtime Revert failure.")
    };
}

public enum RevertRuntimeRejectionReason
{
    OperationInProgress,
    BaselineNotInitialized
}

public sealed record RevertRuntimeRejection(RevertRuntimeRejectionReason Reason);
