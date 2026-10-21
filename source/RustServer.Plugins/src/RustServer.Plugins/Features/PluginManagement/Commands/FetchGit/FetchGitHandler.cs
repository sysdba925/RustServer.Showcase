using RustServer.Plugins.Features.PluginManagement.Git;
using RustServer.Plugins.Features.PluginManagement.Guards;
using RustServer.Plugins.Features.PluginManagement.LocalGit;
using RustServer.Plugins.Features.PluginManagement.Models.Git;
using RustServer.Plugins.Features.PluginManagement.Models.Operations;
using RustServer.Plugins.Features.PluginManagement.Operations;
using RustServer.Plugins.Features.PluginManagement.RemoteGit;

namespace RustServer.Plugins.Features.PluginManagement.Commands.FetchGit;

public sealed class FetchGitHandler(
    IRemoteGit remoteGit,
    ILocalGit localGit,
    LastFetchState lastFetchState,
    IExclusiveMutationGuard mutationGuard,
    IOperationReports operationReports)
{
    public async Task<FetchGitResult> HandleAsync(CancellationToken cancellationToken)
    {
        var lease = mutationGuard.TryAcquire();
        if (lease is null)
        {
            return new FetchGitResult(
                OperationId: null,
                RepositoryResults: [],
                Failure: null,
                Rejection: new FetchGitRejection(FetchGitRejectionReason.OperationInProgress));
        }

        await using (lease)
        {

            var report = operationReports.Begin(
                OperationType.FetchGit,
                source: null,
                ["fetchPlugins", "fetchConfigs", "captureFinalState"],
                initialPhase: "fetchPlugins");
            var results = new List<RemoteGitFetchResult>();
            var activePhase = "fetchPlugins";
            var activeRepository = RepositoryId.Plugins;

            try
            {
                foreach (var repository in new[] { RepositoryId.Plugins, RepositoryId.Configs })
                {
                    var stepName = GetFetchStepName(repository);
                    activePhase = stepName;
                    activeRepository = repository;
                    report.SetPhase(stepName);
                    RemoteGitFetchResult fetchResult;

                    try
                    {
                        fetchResult = await remoteGit.FetchAsync(repository, cancellationToken);
                    }
                    catch (RemoteGitException)
                    {
                        return await FailAsync(
                            report,
                            results,
                            stepName,
                            new FetchGitFailure(repository, FetchGitFailureReason.RepositoryUnavailable));
                    }

                    results.Add(fetchResult);
                    if (!fetchResult.Succeeded)
                    {
                        return await FailAsync(
                            report,
                            results,
                            stepName,
                            new FetchGitFailure(repository, ToFailureReason(fetchResult.Status)));
                    }

                    report.CompleteStep(stepName);
                }

                lastFetchState.Set(DateTimeOffset.UtcNow);

                activePhase = "captureFinalState";
                await CaptureFinalGitStateAsync(report, cancellationToken);
                report.Succeed();

                return new FetchGitResult(report.Id, results, Failure: null);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return await FailAsync(
                    report,
                    results,
                    activePhase,
                    new FetchGitFailure(activeRepository, FetchGitFailureReason.OperationCanceled));
            }
        }
    }

    private async Task<FetchGitResult> FailAsync(
        OperationReportTracker report,
        IReadOnlyList<RemoteGitFetchResult> results,
        string failedStep,
        FetchGitFailure fetchFailure)
    {
        report.FailStep(failedStep);
        await CaptureFinalGitStateAsync(report, CancellationToken.None);
        report.Fail(new OperationFailure(
            failedStep,
            fetchFailure.ErrorCode,
            fetchFailure.ErrorMessage,
            fetchFailure.Repository));

        return new FetchGitResult(report.Id, results, fetchFailure);
    }

    private async Task CaptureFinalGitStateAsync(
        OperationReportTracker report,
        CancellationToken cancellationToken)
    {
        report.SetPhase("captureFinalState");
        var states = new List<OperationGitState>();

        foreach (var repository in new[] { RepositoryId.Plugins, RepositoryId.Configs })
        {
            try
            {
                var state = await localGit.GetStateAsync(repository, cancellationToken);
                states.Add(new OperationGitState(
                    repository,
                    state.Branch,
                    state.Head,
                    state.Relation,
                    ToDiagnostic(state)));
            }
            catch (LocalGitException)
            {
                states.Add(new OperationGitState(
                    repository,
                    Branch: null,
                    Head: null,
                    LocalGitRelation.Unknown,
                    Diagnostic: "unavailable"));
            }
        }

        report.SetFinalGitStates(states);
        report.CompleteStep("captureFinalState");
    }

    private static string GetFetchStepName(RepositoryId repository) =>
        repository switch
        {
            RepositoryId.Plugins => "fetchPlugins",
            RepositoryId.Configs => "fetchConfigs",
            _ => throw new ArgumentOutOfRangeException(nameof(repository), repository, "Unsupported repository.")
        };

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

    private static FetchGitFailureReason ToFailureReason(RemoteGitFetchStatus status) =>
        status switch
        {
            RemoteGitFetchStatus.WorkspaceMissing or RemoteGitFetchStatus.NotRepository =>
                FetchGitFailureReason.RepositoryUnavailable,
            RemoteGitFetchStatus.RemoteMissing => FetchGitFailureReason.RemoteMissing,
            RemoteGitFetchStatus.Failed => FetchGitFailureReason.RemoteFetchFailed,
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unsupported fetch status.")
        };
}
