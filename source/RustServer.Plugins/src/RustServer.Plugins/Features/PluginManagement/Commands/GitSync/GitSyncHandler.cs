using Microsoft.Extensions.Options;
using RustServer.Plugins.Features.PluginManagement.Guards;
using RustServer.Plugins.Features.PluginManagement.LocalGit;
using RustServer.Plugins.Features.PluginManagement.Models;
using RustServer.Plugins.Features.PluginManagement.Models.Git;
using RustServer.Plugins.Features.PluginManagement.Models.Operations;
using RustServer.Plugins.Features.PluginManagement.Operations;
using RustServer.Plugins.Features.PluginManagement.RemoteGit;

namespace RustServer.Plugins.Features.PluginManagement.Commands.GitSync;

public sealed class GitSyncHandler(
    IExclusiveMutationGuard mutationGuard,
    IRemoteGit remoteGit,
    ILocalGit localGit,
    IOperationReports operationReports,
    IOptions<GitCommitAuthorOptions> gitCommitAuthorOptions)
{
    private static readonly string[] StepNames =
    [
        "fetchRemote",
        "inspectState",
        "synchronize",
        "refreshBeforePush",
        "push",
        "captureFinalState"
    ];

    public async Task<GitSyncResult> HandleAsync(
        GitSyncCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var lease = mutationGuard.TryAcquire();
        if (lease is null)
        {
            return Rejected(GitSyncRejectionReason.OperationInProgress);
        }

        await using (lease)
        {

            var report = operationReports.Begin(
                OperationType.GitSync,
                source: null,
                StepNames,
                initialPhase: "fetchRemote");
            report.SetSync(new OperationSyncDetails(
                command.Repository,
                LocalGitRelation.Unknown,
                OperationSyncBehavior.None,
                ManualInterventionRequired: false));

            var activePhase = "fetchRemote";
            LocalGitRepositoryState? originalState = null;
            var mergeRecoveryRequired = false;

            try
            {
                var fetchResult = await remoteGit.FetchAsync(command.Repository, cancellationToken);
                if (!fetchResult.Succeeded)
                {
                    return await FailAsync(
                        report,
                        new GitSyncFailure(ToFailureReason(fetchResult.Status), "fetchRemote", command.Repository));
                }

                report.CompleteStep("fetchRemote");
                activePhase = "inspectState";
                report.SetPhase(activePhase);

                originalState = await localGit.GetStateAsync(command.Repository, cancellationToken);
                if (!TryGetSource(originalState, out var source))
                {
                    report.SetSync(new OperationSyncDetails(
                        command.Repository,
                        originalState.Relation,
                        OperationSyncBehavior.None,
                        ManualInterventionRequired: false));
                    return await FailAsync(
                        report,
                        new GitSyncFailure(GitSyncFailureReason.GitWorkspaceUnsafe, activePhase, command.Repository));
                }

                if (originalState.Relation is not (LocalGitRelation.Equal or
                    LocalGitRelation.Behind or
                    LocalGitRelation.Ahead or
                    LocalGitRelation.Diverged))
                {
                    return await FailAsync(
                        report,
                        new GitSyncFailure(GitSyncFailureReason.GitWorkspaceUnsafe, activePhase, command.Repository));
                }

                var behavior = ToBehavior(originalState.Relation);
                report.SetSync(new OperationSyncDetails(
                    command.Repository,
                    originalState.Relation,
                    behavior,
                    ManualInterventionRequired: false));
                report.CompleteStep(activePhase);

                activePhase = "synchronize";
                report.SetPhase(activePhase);
                switch (behavior)
                {
                    case OperationSyncBehavior.None:
                        report.CompleteStep(activePhase);
                        await CaptureFinalGitStateAsync(report, command.Repository, CancellationToken.None);
                        report.Succeed();
                        return new GitSyncResult(report.Id, Failure: null);

                    case OperationSyncBehavior.FastForward:
                        await localGit.FastForwardCurrentBranchAsync(command.Repository, cancellationToken);
                        report.CompleteStep(activePhase);
                        await CaptureFinalGitStateAsync(report, command.Repository, CancellationToken.None);
                        report.Succeed();
                        return new GitSyncResult(report.Id, Failure: null);

                    case OperationSyncBehavior.Push:
                        report.CompleteStep(activePhase);
                        break;

                    case OperationSyncBehavior.MergeAndPush:
                        mergeRecoveryRequired = true;
                        var mergeResult = await localGit.MergeCurrentBranchWithUpstreamAsync(
                            command.Repository,
                            gitCommitAuthorOptions.Value.Name,
                            gitCommitAuthorOptions.Value.Email,
                            cancellationToken);
                        if (mergeResult.Status is LocalGitMergeStatus.Conflict)
                        {
                            return await RecoverFailedMergeAsync(
                                report,
                                new GitSyncFailure(GitSyncFailureReason.GitMergeConflict, activePhase, command.Repository),
                                originalState);
                        }

                        if (mergeResult.Status is not LocalGitMergeStatus.Succeeded)
                        {
                            return await RecoverFailedMergeAsync(
                                report,
                                new GitSyncFailure(GitSyncFailureReason.GitSyncFailed, activePhase, command.Repository),
                                originalState);
                        }

                        mergeRecoveryRequired = false;
                        report.CompleteStep(activePhase);
                        break;

                    default:
                        throw new ArgumentOutOfRangeException(nameof(behavior), behavior, "Unsupported Git Sync behavior.");
                }

                activePhase = "refreshBeforePush";
                report.SetPhase(activePhase);
                var refreshResult = await remoteGit.FetchAsync(command.Repository, cancellationToken);
                if (!refreshResult.Succeeded)
                {
                    return await FailAsync(
                        report,
                        new GitSyncFailure(ToFailureReason(refreshResult.Status), activePhase, command.Repository));
                }

                var stateBeforePush = await localGit.GetStateAsync(command.Repository, cancellationToken);
                if (!IsSafePushState(stateBeforePush, source))
                {
                    return await FailAsync(
                        report,
                        new GitSyncFailure(GitSyncFailureReason.GitRemoteChanged, activePhase, command.Repository));
                }

                report.CompleteStep(activePhase);
                activePhase = "push";
                report.SetPhase(activePhase);
                var pushResult = await remoteGit.PushAsync(command.Repository, source, cancellationToken);
                if (!pushResult.Succeeded)
                {
                    var failure = pushResult.Status is RemoteGitPushStatus.Failed
                        ? await DiagnoseFailedPushAsync(command.Repository, source)
                        : new GitSyncFailure(ToFailureReason(pushResult.Status), activePhase, command.Repository);
                    return await FailAsync(
                        report,
                        failure);
                }

                report.CompleteStep(activePhase);
                await CaptureFinalGitStateAsync(report, command.Repository, CancellationToken.None);
                report.Succeed();
                return new GitSyncResult(report.Id, Failure: null);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return await FailAfterPossibleMergeAsync(
                    report,
                    new GitSyncFailure(GitSyncFailureReason.OperationCanceled, activePhase, command.Repository),
                    originalState,
                    mergeRecoveryRequired);
            }
            catch (RemoteGitException)
            {
                return await FailAfterPossibleMergeAsync(
                    report,
                    new GitSyncFailure(GitSyncFailureReason.GitRepositoryUnavailable, activePhase, command.Repository),
                    originalState,
                    mergeRecoveryRequired);
            }
            catch (LocalGitException)
            {
                return await FailAfterPossibleMergeAsync(
                    report,
                    new GitSyncFailure(GitSyncFailureReason.GitSyncFailed, activePhase, command.Repository),
                    originalState,
                    mergeRecoveryRequired);
            }
        }
    }

    private async Task<GitSyncResult> RecoverFailedMergeAsync(
        OperationReportTracker report,
        GitSyncFailure failure,
        LocalGitRepositoryState originalState)
    {
        report.FailStep(failure.Phase);
        try
        {
            if (await localGit.IsMergeInProgressAsync(failure.Repository, CancellationToken.None))
            {
                await localGit.AbortMergeAsync(failure.Repository, originalState, CancellationToken.None);
                report.SetRollback(new RollbackResult(
                    RollbackStatus.Succeeded,
                    "Unfinished Git merge was aborted and the original workspace was restored."));
            }
            else if (await IsOriginalMergeStateRestoredAsync(failure.Repository, originalState))
            {
                report.SetRollback(new RollbackResult(
                    RollbackStatus.Succeeded,
                    "No unfinished Git merge remained and the original workspace was verified."));
            }
            else
            {
                return await FailMergeRecoveryAsync(report, failure, originalState);
            }

            return await FailAsync(
                report,
                failure,
                stepAlreadyFailed: true);
        }
        catch (LocalGitException)
        {
            return await FailMergeRecoveryAsync(report, failure, originalState);
        }
    }

    private async Task<GitSyncResult> FailAfterPossibleMergeAsync(
        OperationReportTracker report,
        GitSyncFailure failure,
        LocalGitRepositoryState? originalState,
        bool mergeRecoveryRequired)
    {
        if (mergeRecoveryRequired && originalState is not null)
        {
            return await RecoverFailedMergeAsync(report, failure, originalState);
        }

        return await FailAsync(report, failure);
    }

    private async Task<GitSyncResult> FailMergeRecoveryAsync(
        OperationReportTracker report,
        GitSyncFailure failure,
        LocalGitRepositoryState originalState)
    {
        report.SetSync(new OperationSyncDetails(
            failure.Repository,
            originalState.Relation,
            OperationSyncBehavior.MergeAndPush,
            ManualInterventionRequired: true));
        report.SetRollback(new RollbackResult(
            RollbackStatus.Failed,
            "Git merge recovery did not restore the original workspace."));
        return await FailAsync(
            report,
            new GitSyncFailure(GitSyncFailureReason.GitMergeRecoveryFailed, failure.Phase, failure.Repository),
            stepAlreadyFailed: true);
    }

    private async Task<bool> IsOriginalMergeStateRestoredAsync(
        RepositoryId repository,
        LocalGitRepositoryState originalState)
    {
        var currentState = await localGit.GetStateAsync(repository, CancellationToken.None);
        return currentState.IsRepository &&
               currentState.IsClean is true &&
               currentState.Diagnostic is LocalGitDiagnostic.None &&
               string.Equals(currentState.Branch, originalState.Branch, StringComparison.Ordinal) &&
               string.Equals(currentState.Head?.Value, originalState.Head?.Value, StringComparison.Ordinal);
    }

    private async Task<GitSyncFailure> DiagnoseFailedPushAsync(
        RepositoryId repository,
        Source source)
    {
        try
        {
            var fetchResult = await remoteGit.FetchAsync(repository, CancellationToken.None);
            if (fetchResult.Succeeded)
            {
                var state = await localGit.GetStateAsync(repository, CancellationToken.None);
                if (IsRemoteAdvanceAfterFailedPush(state, source))
                {
                    return new GitSyncFailure(GitSyncFailureReason.GitRemoteChanged, "push", repository);
                }
            }
        }
        catch (RemoteGitException)
        {
            // A failed diagnostic Fetch cannot identify the reason for the failed Push.
        }
        catch (LocalGitException)
        {
            // A failed local state inspection cannot identify the reason for the failed Push.
        }

        return new GitSyncFailure(GitSyncFailureReason.GitSyncFailed, "push", repository);
    }

    private async Task<GitSyncResult> FailAsync(
        OperationReportTracker report,
        GitSyncFailure failure,
        bool stepAlreadyFailed = false)
    {
        if (!stepAlreadyFailed)
        {
            report.FailStep(failure.Phase);
        }

        await CaptureFinalGitStateAsync(report, failure.Repository, CancellationToken.None);
        report.Fail(new OperationFailure(
            failure.Phase,
            failure.ErrorCode,
            failure.ErrorMessage,
            failure.Repository));
        return new GitSyncResult(report.Id, failure);
    }

    private async Task CaptureFinalGitStateAsync(
        OperationReportTracker report,
        RepositoryId repository,
        CancellationToken cancellationToken)
    {
        report.SetPhase("captureFinalState");
        try
        {
            var state = await localGit.GetStateAsync(repository, cancellationToken);
            report.SetFinalGitStates(
            [
                new OperationGitState(
                    repository,
                    state.Branch,
                    state.Head,
                    state.Relation,
                    ToDiagnostic(state))
            ]);
        }
        catch (LocalGitException)
        {
            report.SetFinalGitStates(
            [
                new OperationGitState(
                    repository,
                    Branch: null,
                    Head: null,
                    LocalGitRelation.Unknown,
                    Diagnostic: "unavailable")
            ]);
        }

        report.CompleteStep("captureFinalState");
    }

    private static bool TryGetSource(LocalGitRepositoryState state, out Source source)
    {
        source = Source.Development;
        if (!state.IsRepository ||
            state.IsClean is not true ||
            state.Diagnostic is not LocalGitDiagnostic.None ||
            state.Branch is null ||
            !string.Equals(state.TrackingBranch, $"origin/{state.Branch}", StringComparison.Ordinal))
        {
            return false;
        }

        if (string.Equals(state.Branch, Source.DevelopmentBranchName, StringComparison.Ordinal))
        {
            return true;
        }

        if (!state.Branch.StartsWith(Release.BranchPrefix, StringComparison.Ordinal) ||
            state.Branch.Length == Release.BranchPrefix.Length)
        {
            return false;
        }

        source = Source.ForRelease(new Release(state.Branch[Release.BranchPrefix.Length..]));
        return true;
    }

    private static OperationSyncBehavior ToBehavior(LocalGitRelation relation) => relation switch
    {
        LocalGitRelation.Equal => OperationSyncBehavior.None,
        LocalGitRelation.Behind => OperationSyncBehavior.FastForward,
        LocalGitRelation.Ahead => OperationSyncBehavior.Push,
        LocalGitRelation.Diverged => OperationSyncBehavior.MergeAndPush,
        _ => throw new LocalGitException("The local and remote branch relation cannot be synchronized safely.")
    };

    private static bool IsSafePushState(LocalGitRepositoryState state, Source source) =>
        state.IsRepository &&
        state.IsClean is true &&
        state.Diagnostic is LocalGitDiagnostic.None &&
        string.Equals(state.Branch, source.BranchName, StringComparison.Ordinal) &&
        string.Equals(state.TrackingBranch, $"origin/{source.BranchName}", StringComparison.Ordinal) &&
        state.Relation is LocalGitRelation.Ahead;

    private static bool IsRemoteAdvanceAfterFailedPush(LocalGitRepositoryState state, Source source) =>
        state.IsRepository &&
        state.IsClean is true &&
        state.Diagnostic is LocalGitDiagnostic.None &&
        string.Equals(state.Branch, source.BranchName, StringComparison.Ordinal) &&
        string.Equals(state.TrackingBranch, $"origin/{source.BranchName}", StringComparison.Ordinal) &&
        state.Relation is LocalGitRelation.Behind or LocalGitRelation.Diverged;

    private static GitSyncFailureReason ToFailureReason(RemoteGitFetchStatus status) => status switch
    {
        RemoteGitFetchStatus.WorkspaceMissing or RemoteGitFetchStatus.NotRepository =>
            GitSyncFailureReason.GitRepositoryUnavailable,
        RemoteGitFetchStatus.RemoteMissing => GitSyncFailureReason.GitRemoteMissing,
        RemoteGitFetchStatus.Failed => GitSyncFailureReason.GitRemoteFetchFailed,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unsupported Git fetch status.")
    };

    private static GitSyncFailureReason ToFailureReason(RemoteGitPushStatus status) => status switch
    {
        RemoteGitPushStatus.WorkspaceMissing or RemoteGitPushStatus.NotRepository =>
            GitSyncFailureReason.GitRepositoryUnavailable,
        RemoteGitPushStatus.RemoteMissing => GitSyncFailureReason.GitRemoteMissing,
        RemoteGitPushStatus.Failed => GitSyncFailureReason.GitSyncFailed,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unsupported Git push status.")
    };

    private static GitSyncResult Rejected(GitSyncRejectionReason reason) =>
        new(OperationId: null, Failure: null, new GitSyncRejection(reason));

    private static string? ToDiagnostic(LocalGitRepositoryState state)
    {
        if (state.Diagnostic is not LocalGitDiagnostic.None)
        {
            return state.Diagnostic switch
            {
                LocalGitDiagnostic.WorkspaceMissing => "workspaceMissing",
                LocalGitDiagnostic.NotRepository => "notRepository",
                LocalGitDiagnostic.DetachedHead => "detachedHead",
                LocalGitDiagnostic.HeadUnavailable => "headUnavailable",
                LocalGitDiagnostic.UnsupportedBranch => "unsupportedBranch",
                _ => throw new ArgumentOutOfRangeException(nameof(state.Diagnostic), state.Diagnostic, "Unsupported local Git diagnostic.")
            };
        }

        return state.Relation is LocalGitRelation.NoTracking ? "noTracking" : null;
    }
}
