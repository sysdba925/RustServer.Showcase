using Microsoft.Extensions.Options;
using RustServer.Plugins.Features.PluginManagement.Baseline;
using RustServer.Plugins.Features.PluginManagement.Guards;
using RustServer.Plugins.Features.PluginManagement.LocalGit;
using RustServer.Plugins.Features.PluginManagement.Models;
using RustServer.Plugins.Features.PluginManagement.Models.Git;
using RustServer.Plugins.Features.PluginManagement.Models.Operations;
using RustServer.Plugins.Features.PluginManagement.Operations;
using RustServer.Plugins.Features.PluginManagement.RemoteGit;
using RustServer.Plugins.Features.PluginManagement.Runtime;
using RustServer.Plugins.Features.PluginManagement.RuntimeChanges;
using RustServer.Plugins.Infrastructure.EnvironmentState;
using BaselineModel = RustServer.Plugins.Features.PluginManagement.Models.Baseline;

namespace RustServer.Plugins.Features.PluginManagement.Commands.CommitRuntime;

public sealed class CommitRuntimeHandler(
    IExclusiveMutationGuard mutationGuard,
    IEnvironmentStore environmentStore,
    IBaselineStore baselineStore,
    IRemoteGit remoteGit,
    ILocalGit localGit,
    IRuntimeFiles runtimeFiles,
    RuntimeDiffReader runtimeDiffReader,
    IOperationReports operationReports,
    IOptions<GitCommitAuthorOptions> gitCommitAuthorOptions)
{
    private static readonly RepositoryId[] Repositories = [RepositoryId.Plugins, RepositoryId.Configs];

    public async Task<CommitRuntimeResult> HandleAsync(
        CommitRuntimeCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var lease = mutationGuard.TryAcquire();
        if (lease is null)
        {
            return Rejected(CommitRuntimeRejectionReason.OperationInProgress);
        }

        await using (lease)
        {

            var environment = await environmentStore.GetAsync(cancellationToken);
            if (environment is not PluginEnvironment.Staging)
            {
                return Rejected(CommitRuntimeRejectionReason.CommitNotAllowedInProduction);
            }

            var baseline = await baselineStore.GetAsync(cancellationToken);
            if (baseline is null)
            {
                return Rejected(CommitRuntimeRejectionReason.BaselineNotInitialized);
            }

            var selectedRepositories = GetSelectedRepositories(command.Selections);
            var report = operationReports.Begin(
                OperationType.CommitRuntime,
                baseline.Source,
                CreateStepNames(selectedRepositories),
                initialPhase: "fetchPlugins");
            report.SetFinalBaseline(baseline, final: baseline);
            var commit = CreateCommit(
                command.Message,
                report.Id,
                environment,
                baseline.Source,
                gitCommitAuthorOptions.Value);

            var previousStates = new Dictionary<RepositoryId, LocalGitRepositoryState>();
            var preparations = new Dictionary<RepositoryId, LocalGitSourcePreparation>();
            var workspaceChanges = new Dictionary<RepositoryId, IReadOnlyList<LocalGitWorkingChange>>();
            var createdCommits = new Dictionary<RepositoryId, GitCommitSha>();
            var pushedRepositories = new HashSet<RepositoryId>();
            var activePhase = "fetchPlugins";

            void SetPhase(string phase)
            {
                activePhase = phase;
                report.SetPhase(phase);
            }

            try
            {
                if (string.IsNullOrWhiteSpace(command.Message) || command.Selections.Count == 0)
                {
                    return await FailAsync(
                        report,
                        new CommitRuntimeFailure(CommitRuntimeFailureReason.InvalidRuntimeSelection, "validateSelection"),
                        baseline,
                        previousStates,
                        preparations,
                        workspaceChanges,
                        createdCommits,
                        pushedRepositories);
                }

                foreach (var repository in Repositories)
                {
                    try
                    {
                        previousStates[repository] = await localGit.GetStateAsync(repository, cancellationToken);
                    }
                    catch (LocalGitException)
                    {
                        return await FailAsync(
                            report,
                            new CommitRuntimeFailure(CommitRuntimeFailureReason.GitWorkspaceUnsafe, activePhase, repository),
                            baseline,
                            previousStates,
                            preparations,
                            workspaceChanges,
                            createdCommits,
                            pushedRepositories);
                    }
                }

                foreach (var repository in Repositories)
                {
                    var stepName = FetchStepName(repository);
                    SetPhase(stepName);
                    RemoteGitFetchResult fetchResult;
                    try
                    {
                        fetchResult = await remoteGit.FetchAsync(repository, cancellationToken);
                    }
                    catch (RemoteGitException)
                    {
                        return await FailAsync(
                            report,
                            new CommitRuntimeFailure(
                                CommitRuntimeFailureReason.GitRepositoryUnavailable,
                                stepName,
                                repository),
                            baseline,
                            previousStates,
                            preparations,
                            workspaceChanges,
                            createdCommits,
                            pushedRepositories);
                    }

                    if (!fetchResult.Succeeded)
                    {
                        return await FailAsync(
                            report,
                            new CommitRuntimeFailure(ToFailureReason(fetchResult.Status), stepName, repository),
                            baseline,
                            previousStates,
                            preparations,
                            workspaceChanges,
                            createdCommits,
                            pushedRepositories);
                    }

                    report.CompleteStep(stepName);
                }

                SetPhase("validateSource");
                bool sourceHeadsMatch;
                try
                {
                    sourceHeadsMatch = await BaselineSourceHeadsMatchAsync(baseline, cancellationToken);
                }
                catch (LocalGitException)
                {
                    return await FailAsync(
                        report,
                        new CommitRuntimeFailure(
                            CommitRuntimeFailureReason.GitRepositoryUnavailable,
                            "validateSource"),
                        baseline,
                        previousStates,
                        preparations,
                        workspaceChanges,
                        createdCommits,
                        pushedRepositories);
                }

                if (!sourceHeadsMatch)
                {
                    return await FailAsync(
                        report,
                        new CommitRuntimeFailure(CommitRuntimeFailureReason.GitSourceChangedSinceBaseline, "validateSource"),
                        baseline,
                        previousStates,
                        preparations,
                        workspaceChanges,
                        createdCommits,
                        pushedRepositories);
                }

                try
                {
                    foreach (var repository in Repositories)
                    {
                        var preparation = await localGit.ValidateSourcePreparationAsync(
                            repository,
                            baseline.Source,
                            GetBaselineCommit(baseline, repository),
                            cancellationToken);
                        preparations[repository] = preparation;
                    }
                }
                catch (LocalGitException)
                {
                    return await FailAsync(
                        report,
                        new CommitRuntimeFailure(CommitRuntimeFailureReason.GitWorkspaceUnsafe, "validateSource"),
                        baseline,
                        previousStates,
                        preparations,
                        workspaceChanges,
                        createdCommits,
                        pushedRepositories);
                }

                if (preparations.Values.Any(preparation =>
                        preparation.Kind is LocalGitSourcePreparationKind.FastForward))
                {
                    preparations.Clear();
                    return await FailAsync(
                        report,
                        new CommitRuntimeFailure(
                            CommitRuntimeFailureReason.GitSynchronizationRequired,
                            "validateSource"),
                        baseline,
                        previousStates,
                        preparations,
                        workspaceChanges,
                        createdCommits,
                        pushedRepositories);
                }

                report.CompleteStep("validateSource");

                foreach (var repository in Repositories)
                {
                    var stepName = PrepareStepName(repository);
                    SetPhase(stepName);
                    try
                    {
                        await localGit.PrepareSourceWithoutSynchronizationAsync(
                            repository,
                            baseline.Source,
                            GetBaselineCommit(baseline, repository),
                            cancellationToken);
                    }
                    catch (LocalGitException)
                    {
                        return await FailAsync(
                            report,
                            new CommitRuntimeFailure(CommitRuntimeFailureReason.GitWorkspaceUnsafe, stepName, repository),
                            baseline,
                            previousStates,
                            preparations,
                            workspaceChanges,
                            createdCommits,
                            pushedRepositories);
                    }

                    report.CompleteStep(stepName);
                }

                SetPhase("validateSelection");
                IReadOnlyList<RuntimeFileChange> selectedChanges;
                IReadOnlyList<SelectedRuntimeSnapshot> selectedSnapshots;
                try
                {
                    selectedChanges = await ResolveSelectedChangesAsync(command.Selections, cancellationToken);
                    selectedSnapshots = await CaptureSelectedRuntimeSnapshotsAsync(selectedChanges, cancellationToken);
                }
                catch (RuntimeDiffException exception)
                {
                    return await FailAsync(
                        report,
                        new CommitRuntimeFailure(
                            exception.Failure is RuntimeDiffFailure.BaselineGitObjectUnavailable
                                ? CommitRuntimeFailureReason.BaselineGitObjectUnavailable
                                : CommitRuntimeFailureReason.RuntimeUnavailable,
                            "validateSelection",
                            exception.Repository),
                        baseline,
                        previousStates,
                        preparations,
                        workspaceChanges,
                        createdCommits,
                        pushedRepositories);
                }
                catch (RuntimeFilesException)
                {
                    return await FailAsync(
                        report,
                        new CommitRuntimeFailure(
                            CommitRuntimeFailureReason.RuntimeUnavailable,
                            "validateSelection"),
                        baseline,
                        previousStates,
                        preparations,
                        workspaceChanges,
                        createdCommits,
                        pushedRepositories);
                }
                catch (IOException)
                {
                    return await FailAsync(
                        report,
                        new CommitRuntimeFailure(
                            CommitRuntimeFailureReason.RuntimeUnavailable,
                            "validateSelection"),
                        baseline,
                        previousStates,
                        preparations,
                        workspaceChanges,
                        createdCommits,
                        pushedRepositories);
                }
                catch (UnauthorizedAccessException)
                {
                    return await FailAsync(
                        report,
                        new CommitRuntimeFailure(
                            CommitRuntimeFailureReason.RuntimeUnavailable,
                            "validateSelection"),
                        baseline,
                        previousStates,
                        preparations,
                        workspaceChanges,
                        createdCommits,
                        pushedRepositories);
                }
                catch (InvalidSelectionException exception)
                {
                    return await FailAsync(
                        report,
                        new CommitRuntimeFailure(
                            CommitRuntimeFailureReason.InvalidRuntimeSelection,
                            "validateSelection",
                            exception.Repository,
                            exception.Path),
                        baseline,
                        previousStates,
                        preparations,
                        workspaceChanges,
                        createdCommits,
                        pushedRepositories);
                }

                report.SetChanges(selectedChanges.Select(ToOperationChange).ToArray());
                report.CompleteStep("validateSelection");

                foreach (var repository in selectedRepositories)
                {
                    var changes = selectedChanges
                        .Where(change => change.File.Repository == repository)
                        .ToArray();
                    workspaceChanges[repository] = changes.Select(ToWorkingChange).ToArray();
                    var stepName = ApplyChangesStepName(repository);
                    SetPhase(stepName);

                    try
                    {
                        foreach (var change in changes)
                        {
                            if (change.Change is RuntimeChangeKind.Deleted)
                            {
                                await localGit.RemoveWorkingFileAsync(
                                    repository,
                                    change.File.RepositoryRelativePath,
                                    cancellationToken);
                            }
                            else
                            {
                                var content = selectedSnapshots
                                    .Single(snapshot => IsSameFile(snapshot.Change.File, change.File))
                                    .Content ?? throw new InvalidOperationException("A Runtime content snapshot is missing.");
                                await localGit.WriteWorkingFileAsync(
                                    repository,
                                    change.File.RepositoryRelativePath,
                                    content,
                                    cancellationToken);
                            }
                        }
                    }
                    catch (RuntimeFilesException)
                    {
                        return await FailAsync(
                            report,
                            new CommitRuntimeFailure(CommitRuntimeFailureReason.RuntimeUnavailable, stepName, repository),
                            baseline,
                            previousStates,
                            preparations,
                            workspaceChanges,
                            createdCommits,
                            pushedRepositories);
                    }
                    catch (LocalGitException)
                    {
                        return await FailAsync(
                            report,
                            new CommitRuntimeFailure(CommitRuntimeFailureReason.GitWorkspaceUnsafe, stepName, repository),
                            baseline,
                            previousStates,
                            preparations,
                            workspaceChanges,
                            createdCommits,
                            pushedRepositories);
                    }

                    report.CompleteStep(stepName);
                }

                foreach (var repository in selectedRepositories)
                {
                    var stepName = StageStepName(repository);
                    SetPhase(stepName);
                    var changes = selectedChanges.Where(change => change.File.Repository == repository).ToArray();
                    try
                    {
                        foreach (var change in changes.Where(change => change.Change is not RuntimeChangeKind.Deleted))
                        {
                            await localGit.StageWorkingFileAsync(
                                repository,
                                change.File.RepositoryRelativePath,
                                cancellationToken);
                        }

                        var inspection = await localGit.InspectStagingAsync(repository, cancellationToken);
                        if (!HasExpectedStaging(inspection, changes))
                        {
                            throw new LocalGitException("The staged or working-tree paths do not match the selected Runtime changes.");
                        }
                    }
                    catch (LocalGitException)
                    {
                        return await FailAsync(
                            report,
                            new CommitRuntimeFailure(CommitRuntimeFailureReason.GitWorkspaceUnsafe, stepName, repository),
                            baseline,
                            previousStates,
                            preparations,
                            workspaceChanges,
                            createdCommits,
                            pushedRepositories);
                    }

                    report.CompleteStep(stepName);
                }

                foreach (var repository in selectedRepositories)
                {
                    var stepName = CommitStepName(repository);
                    SetPhase(stepName);
                    try
                    {
                        createdCommits[repository] = await localGit.CreateCommitAsync(
                            repository,
                            commit,
                            cancellationToken);
                    }
                    catch (LocalGitException)
                    {
                        return await FailAsync(
                            report,
                            new CommitRuntimeFailure(CommitRuntimeFailureReason.GitCommitFailed, stepName, repository),
                            baseline,
                            previousStates,
                            preparations,
                            workspaceChanges,
                            createdCommits,
                            pushedRepositories);
                    }

                    report.CompleteStep(stepName);
                }

                foreach (var repository in selectedRepositories)
                {
                    var stepName = PushStepName(repository);
                    SetPhase(stepName);
                    RemoteGitPushResult pushResult;
                    try
                    {
                        pushResult = await remoteGit.PushAsync(repository, baseline.Source, cancellationToken);
                    }
                    catch (RemoteGitException)
                    {
                        return await FailAsync(
                            report,
                            new CommitRuntimeFailure(
                                CommitRuntimeFailureReason.GitRepositoryUnavailable,
                                stepName,
                                repository),
                            baseline,
                            previousStates,
                            preparations,
                            workspaceChanges,
                            createdCommits,
                            pushedRepositories);
                    }

                    if (!pushResult.Succeeded)
                    {
                        return await FailAsync(
                            report,
                            new CommitRuntimeFailure(ToFailureReason(pushResult.Status), stepName, repository),
                            baseline,
                            previousStates,
                            preparations,
                            workspaceChanges,
                            createdCommits,
                            pushedRepositories);
                    }

                    pushedRepositories.Add(repository);
                    report.CompleteStep(stepName);
                }

                SetPhase("persistBaseline");
                var committedBaseline = new BaselineModel(
                    environment,
                    baseline.Source,
                    GetCommittedOrBaselineCommit(baseline, createdCommits, RepositoryId.Plugins),
                    GetCommittedOrBaselineCommit(baseline, createdCommits, RepositoryId.Configs),
                    DateTimeOffset.UtcNow);
                try
                {
                    await baselineStore.SaveAsync(committedBaseline, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or BaselineStateException)
                {
                    return await FailAsync(
                        report,
                        new CommitRuntimeFailure(CommitRuntimeFailureReason.BaselinePersistFailed, "persistBaseline"),
                        baseline,
                        previousStates,
                        preparations,
                        workspaceChanges,
                        createdCommits,
                        pushedRepositories);
                }

                report.CompleteStep("persistBaseline");
                await CaptureFinalStateAsync(report, baseline, CancellationToken.None);
                report.Succeed();
                return new CommitRuntimeResult(report.Id, Failure: null);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return await FailAsync(
                    report,
                    new CommitRuntimeFailure(CommitRuntimeFailureReason.OperationCanceled, activePhase),
                    baseline,
                    previousStates,
                    preparations,
                    workspaceChanges,
                    createdCommits,
                    pushedRepositories);
            }
        }
    }

    private async Task<bool> BaselineSourceHeadsMatchAsync(
        BaselineModel baseline,
        CancellationToken cancellationToken)
    {
        var plugins = await localGit.GetKnownRemoteSourceHeadAsync(
            RepositoryId.Plugins,
            baseline.Source,
            cancellationToken);
        var configs = await localGit.GetKnownRemoteSourceHeadAsync(
            RepositoryId.Configs,
            baseline.Source,
            cancellationToken);

        return plugins is not null && configs is not null &&
               plugins == baseline.PluginsCommit && configs == baseline.ConfigsCommit;
    }

    private async Task<IReadOnlyList<RuntimeFileChange>> ResolveSelectedChangesAsync(
        IReadOnlyList<CommitRuntimeSelection> selections,
        CancellationToken cancellationToken)
    {
        var runtimeDiff = await runtimeDiffReader.GetAsync(cancellationToken);
        if (runtimeDiff.Status is not RuntimeDiffStatus.Ready)
        {
            throw new InvalidSelectionException();
        }

        var runtimeChanges = runtimeDiff.Changes.ToDictionary(
            change => new RuntimeChangeKey(change.File.Repository, change.File.RuntimeRelativePath));
        var selected = new List<RuntimeFileChange>(selections.Count);
        var seen = new HashSet<RuntimeChangeKey>();

        foreach (var selection in selections)
        {
            var key = new RuntimeChangeKey(selection.File.Repository, selection.File.RuntimeRelativePath);
            if (!seen.Add(key) || !runtimeChanges.TryGetValue(key, out var change))
            {
                throw new InvalidSelectionException(selection.File.Repository, selection.File.RuntimeRelativePath);
            }

            selected.Add(change);
        }

        return selected;
    }

    private async Task<IReadOnlyList<SelectedRuntimeSnapshot>> CaptureSelectedRuntimeSnapshotsAsync(
        IReadOnlyList<RuntimeFileChange> selectedChanges,
        CancellationToken cancellationToken)
    {
        try
        {
            var snapshots = new List<SelectedRuntimeSnapshot>(selectedChanges.Count);
            foreach (var change in selectedChanges)
            {
                var content = change.Change is RuntimeChangeKind.Deleted
                    ? null
                    : await runtimeFiles.ReadAsync(change.File, cancellationToken);
                snapshots.Add(new SelectedRuntimeSnapshot(change, content));
            }

            var refreshedDiff = await runtimeDiffReader.GetAsync(cancellationToken);
            if (refreshedDiff.Status is not RuntimeDiffStatus.Ready)
            {
                throw new InvalidSelectionException();
            }

            var refreshedByFile = refreshedDiff.Changes.ToDictionary(
                change => new RuntimeChangeKey(change.File.Repository, change.File.RuntimeRelativePath));
            foreach (var snapshot in snapshots)
            {
                var key = new RuntimeChangeKey(
                    snapshot.Change.File.Repository,
                    snapshot.Change.File.RuntimeRelativePath);
                if (!refreshedByFile.TryGetValue(key, out var refreshedChange) ||
                    refreshedChange.Change != snapshot.Change.Change)
                {
                    throw new InvalidSelectionException(
                        snapshot.Change.File.Repository,
                        snapshot.Change.File.RuntimeRelativePath);
                }

                if (snapshot.Content is not null)
                {
                    var currentContent = await runtimeFiles.ReadAsync(snapshot.Change.File, cancellationToken);
                    if (!currentContent.AsSpan().SequenceEqual(snapshot.Content))
                    {
                        throw new InvalidSelectionException(
                            snapshot.Change.File.Repository,
                            snapshot.Change.File.RuntimeRelativePath);
                    }
                }
            }

            return snapshots;
        }
        catch (FileNotFoundException)
        {
            throw new InvalidSelectionException();
        }
        catch (DirectoryNotFoundException)
        {
            throw new InvalidSelectionException();
        }
    }

    private async Task<CommitRuntimeResult> FailAsync(
        OperationReportTracker report,
        CommitRuntimeFailure failure,
        BaselineModel baseline,
        IReadOnlyDictionary<RepositoryId, LocalGitRepositoryState> previousStates,
        IReadOnlyDictionary<RepositoryId, LocalGitSourcePreparation> preparations,
        IReadOnlyDictionary<RepositoryId, IReadOnlyList<LocalGitWorkingChange>> workspaceChanges,
        IReadOnlyDictionary<RepositoryId, GitCommitSha> createdCommits,
        IReadOnlySet<RepositoryId> pushedRepositories)
    {
        report.FailStep(failure.Phase);
        var rollbackMessages = new List<string>();
        var rollbackAttempted = false;
        var rollbackSucceeded = true;

        foreach (var repository in Repositories.Where(repository => !pushedRepositories.Contains(repository)))
        {
            if (createdCommits.TryGetValue(repository, out var createdCommit))
            {
                rollbackAttempted = true;
                try
                {
                    await localGit.RestoreCreatedCommitAsync(
                        repository,
                        baseline.Source,
                        GetBaselineCommit(baseline, repository),
                        createdCommit,
                        CancellationToken.None);
                    rollbackMessages.Add($"{repository} unpushed local commit was removed.");
                }
                catch (LocalGitException exception)
                {
                    rollbackSucceeded = false;
                    rollbackMessages.Add($"{repository} unpushed local commit rollback failed: {exception.Message}");
                }

                continue;
            }

            if (!workspaceChanges.TryGetValue(repository, out var changes) || changes.Count == 0)
            {
                continue;
            }

            rollbackAttempted = true;
            try
            {
                await localGit.RestoreOperationChangesAsync(repository, changes, CancellationToken.None);
                rollbackMessages.Add($"{repository} operation-created workspace changes were restored.");
            }
            catch (LocalGitException exception)
            {
                rollbackSucceeded = false;
                rollbackMessages.Add($"{repository} workspace rollback failed: {exception.Message}");
            }
        }

        foreach (var repository in Repositories)
        {
            if (pushedRepositories.Contains(repository) ||
                !preparations.TryGetValue(repository, out var preparation) ||
                !previousStates.TryGetValue(repository, out var previousState))
            {
                continue;
            }

            rollbackAttempted = true;
            try
            {
                await localGit.RestoreSourcePreparationAsync(
                    repository,
                    previousState,
                    preparation,
                    CancellationToken.None);
                rollbackMessages.Add($"{repository} Source workspace was restored.");
            }
            catch (LocalGitException exception)
            {
                rollbackSucceeded = false;
                rollbackMessages.Add($"{repository} Source workspace rollback failed: {exception.Message}");
            }
        }

        foreach (var repository in Repositories.Where(pushedRepositories.Contains))
        {
            rollbackMessages.Add($"{repository} remote push succeeded and was not rolled back.");
        }

        report.SetRollback(rollbackAttempted
            ? new RollbackResult(
                rollbackSucceeded ? RollbackStatus.Succeeded : RollbackStatus.Failed,
                string.Join(' ', rollbackMessages))
            : new RollbackResult(RollbackStatus.NotAttempted));
        await CaptureFinalStateAsync(report, baseline, CancellationToken.None);
        report.Fail(new OperationFailure(
            failure.Phase,
            failure.ErrorCode,
            failure.ErrorMessage,
            failure.Repository,
            failure.FilePath));

        return new CommitRuntimeResult(report.Id, failure);
    }

    private async Task CaptureFinalStateAsync(
        OperationReportTracker report,
        BaselineModel baseline,
        CancellationToken cancellationToken)
    {
        report.SetPhase("captureFinalState");
        var states = new List<OperationGitState>();

        foreach (var repository in Repositories)
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
        try
        {
            report.SetFinalBaseline(baseline, await baselineStore.GetAsync(cancellationToken));
        }
        catch (BaselineStateException)
        {
            report.SetFinalBaseline(baseline, final: null);
        }

        report.CompleteStep("captureFinalState");
    }

    private static CommitRuntimeResult Rejected(CommitRuntimeRejectionReason reason) =>
        new(OperationId: null, Failure: null, new CommitRuntimeRejection(reason));

    private static IReadOnlyList<RepositoryId> GetSelectedRepositories(
        IReadOnlyList<CommitRuntimeSelection> selections) =>
        Repositories.Where(repository => selections.Any(selection => selection.File.Repository == repository)).ToArray();

    private static IReadOnlyList<string> CreateStepNames(IReadOnlyList<RepositoryId> selectedRepositories)
    {
        var steps = new List<string>
        {
            "fetchPlugins",
            "fetchConfigs",
            "validateSource",
            "preparePlugins",
            "prepareConfigs",
            "validateSelection"
        };

        foreach (var repository in selectedRepositories)
        {
            steps.Add(ApplyChangesStepName(repository));
            steps.Add(StageStepName(repository));
            steps.Add(CommitStepName(repository));
            steps.Add(PushStepName(repository));
        }

        steps.Add("persistBaseline");
        steps.Add("captureFinalState");
        return steps;
    }

    private static bool HasExpectedStaging(
        LocalGitStagingInspection inspection,
        IReadOnlyList<RuntimeFileChange> changes)
    {
        if (!inspection.IsCleanOutsideIndex)
        {
            return false;
        }

        var expected = changes
            .Select(change => change.File.RepositoryRelativePath)
            .ToHashSet(StringComparer.Ordinal);
        var staged = inspection.StagedPaths.ToHashSet(StringComparer.Ordinal);
        return expected.SetEquals(staged);
    }

    private static GitCommitSha GetBaselineCommit(BaselineModel baseline, RepositoryId repository) =>
        repository switch
        {
            RepositoryId.Plugins => baseline.PluginsCommit,
            RepositoryId.Configs => baseline.ConfigsCommit,
            _ => throw new ArgumentOutOfRangeException(nameof(repository), repository, "Unsupported repository.")
        };

    private static GitCommitSha GetCommittedOrBaselineCommit(
        BaselineModel baseline,
        IReadOnlyDictionary<RepositoryId, GitCommitSha> createdCommits,
        RepositoryId repository) =>
        createdCommits.TryGetValue(repository, out var committed)
            ? committed
            : GetBaselineCommit(baseline, repository);

    private static LocalGitCommit CreateCommit(
        string message,
        OperationId operationId,
        PluginEnvironment environment,
        Source source,
        GitCommitAuthorOptions author)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        ArgumentNullException.ThrowIfNull(operationId);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(author);

        return new LocalGitCommit(
            $"{message}\n\nRustServer-Operation: {operationId.Value}\nRustServer-Environment: {ToTrailerValue(environment)}\nRustServer-Source: {ToTrailerValue(source)}",
            author.Name,
            author.Email);
    }

    private static string ToTrailerValue(PluginEnvironment environment) => environment switch
    {
        PluginEnvironment.Staging => "staging",
        PluginEnvironment.Production => "production",
        _ => throw new ArgumentOutOfRangeException(nameof(environment), environment, "Unsupported PluginManagement environment.")
    };

    private static string ToTrailerValue(Source source) => source.Kind switch
    {
        SourceKind.Development => "development",
        SourceKind.Release => source.BranchName,
        _ => throw new ArgumentOutOfRangeException(nameof(source), source.Kind, "Unsupported Source kind.")
    };

    private static OperationFileChange ToOperationChange(RuntimeFileChange change) =>
        new(
            change.File.Repository,
            change.File.RuntimeRelativePath,
            change.Change switch
            {
                RuntimeChangeKind.Added => OperationChangeKind.Added,
                RuntimeChangeKind.Modified => OperationChangeKind.Modified,
                RuntimeChangeKind.Deleted => OperationChangeKind.Deleted,
                _ => throw new ArgumentOutOfRangeException(nameof(change.Change), change.Change, "Unsupported Runtime change.")
            });

    private static LocalGitWorkingChange ToWorkingChange(RuntimeFileChange change) =>
        new(
            change.File.RepositoryRelativePath,
            change.Change switch
            {
                RuntimeChangeKind.Added => LocalGitWorkingChangeKind.Added,
                RuntimeChangeKind.Modified => LocalGitWorkingChangeKind.Modified,
                RuntimeChangeKind.Deleted => LocalGitWorkingChangeKind.Deleted,
                _ => throw new ArgumentOutOfRangeException(nameof(change.Change), change.Change, "Unsupported Runtime change.")
            });

    private static bool IsSameFile(ManagedFile left, ManagedFile right) =>
        left.Repository == right.Repository &&
        string.Equals(left.RuntimeRelativePath, right.RuntimeRelativePath, StringComparison.Ordinal);

    private static CommitRuntimeFailureReason ToFailureReason(RemoteGitFetchStatus status) =>
        status switch
        {
            RemoteGitFetchStatus.WorkspaceMissing or RemoteGitFetchStatus.NotRepository =>
                CommitRuntimeFailureReason.GitRepositoryUnavailable,
            RemoteGitFetchStatus.RemoteMissing => CommitRuntimeFailureReason.GitRemoteMissing,
            RemoteGitFetchStatus.Failed => CommitRuntimeFailureReason.GitRemoteFetchFailed,
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unsupported fetch status.")
        };

    private static CommitRuntimeFailureReason ToFailureReason(RemoteGitPushStatus status) =>
        status switch
        {
            RemoteGitPushStatus.WorkspaceMissing or RemoteGitPushStatus.NotRepository =>
                CommitRuntimeFailureReason.GitRepositoryUnavailable,
            RemoteGitPushStatus.RemoteMissing => CommitRuntimeFailureReason.GitRemoteMissing,
            RemoteGitPushStatus.Failed => CommitRuntimeFailureReason.GitPushFailed,
            RemoteGitPushStatus.Succeeded => throw new ArgumentOutOfRangeException(
                nameof(status),
                status,
                "A successful push does not have a failure reason."),
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unsupported push status.")
        };

    private static string FetchStepName(RepositoryId repository) => repository switch
    {
        RepositoryId.Plugins => "fetchPlugins",
        RepositoryId.Configs => "fetchConfigs",
        _ => throw new ArgumentOutOfRangeException(nameof(repository), repository, "Unsupported repository.")
    };

    private static string PrepareStepName(RepositoryId repository) => repository switch
    {
        RepositoryId.Plugins => "preparePlugins",
        RepositoryId.Configs => "prepareConfigs",
        _ => throw new ArgumentOutOfRangeException(nameof(repository), repository, "Unsupported repository.")
    };

    private static string ApplyChangesStepName(RepositoryId repository) => repository switch
    {
        RepositoryId.Plugins => "applyPluginsChanges",
        RepositoryId.Configs => "applyConfigsChanges",
        _ => throw new ArgumentOutOfRangeException(nameof(repository), repository, "Unsupported repository.")
    };

    private static string StageStepName(RepositoryId repository) => repository switch
    {
        RepositoryId.Plugins => "stagePlugins",
        RepositoryId.Configs => "stageConfigs",
        _ => throw new ArgumentOutOfRangeException(nameof(repository), repository, "Unsupported repository.")
    };

    private static string CommitStepName(RepositoryId repository) => repository switch
    {
        RepositoryId.Plugins => "commitPlugins",
        RepositoryId.Configs => "commitConfigs",
        _ => throw new ArgumentOutOfRangeException(nameof(repository), repository, "Unsupported repository.")
    };

    private static string PushStepName(RepositoryId repository) => repository switch
    {
        RepositoryId.Plugins => "pushPlugins",
        RepositoryId.Configs => "pushConfigs",
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

    private readonly record struct RuntimeChangeKey(RepositoryId Repository, string RuntimeRelativePath);

    private sealed record SelectedRuntimeSnapshot(RuntimeFileChange Change, byte[]? Content);

    private sealed class InvalidSelectionException(RepositoryId? repository = null, string? path = null)
        : Exception("Selected Runtime changes are invalid.")
    {
        public RepositoryId? Repository { get; } = repository;

        public string? Path { get; } = path;
    }
}
