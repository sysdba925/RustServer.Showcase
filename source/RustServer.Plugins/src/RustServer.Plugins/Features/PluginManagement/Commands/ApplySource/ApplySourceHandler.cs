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

namespace RustServer.Plugins.Features.PluginManagement.Commands.ApplySource;

public sealed class ApplySourceHandler(
    IExclusiveMutationGuard mutationGuard,
    IEnvironmentStore environmentStore,
    IBaselineStore baselineStore,
    IRemoteGit remoteGit,
    ILocalGit localGit,
    IRuntimeFiles runtimeFiles,
    ManagedRuntimeFileReader managedRuntimeFiles,
    ManagedFileMapping mapping,
    IOperationReports operationReports)
{
    private static readonly RepositoryId[] Repositories = [RepositoryId.Plugins, RepositoryId.Configs];

    private static readonly string[] StepNames =
    [
        "fetchPlugins",
        "fetchConfigs",
        "validateSource",
        "preparePlugins",
        "prepareConfigs",
        "calculateApply",
        "writeRuntime",
        "persistBaseline",
        "captureFinalState"
    ];

    public async Task<ApplySourceResult> HandleAsync(Source targetSource, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(targetSource);

        var lease = mutationGuard.TryAcquire();
        if (lease is null)
        {
            return new ApplySourceResult(
                OperationId: null,
                Failure: null,
                new ApplySourceRejection(ApplySourceRejectionReason.OperationInProgress));
        }

        await using (lease)
        {

            var environment = await environmentStore.GetAsync(cancellationToken);
            if (!EnvironmentSourcePolicy.IsAllowed(environment, targetSource))
            {
                return new ApplySourceResult(
                    OperationId: null,
                    Failure: null,
                    new ApplySourceRejection(ApplySourceRejectionReason.SourceNotAllowed));
            }

            var previousBaseline = await baselineStore.GetAsync(cancellationToken);
            var previousPluginsState = await localGit.GetStateAsync(RepositoryId.Plugins, cancellationToken);
            var previousConfigsState = await localGit.GetStateAsync(RepositoryId.Configs, cancellationToken);
            var previousStates = new Dictionary<RepositoryId, LocalGitRepositoryState>
            {
                [RepositoryId.Plugins] = previousPluginsState,
                [RepositoryId.Configs] = previousConfigsState
            };
            var report = operationReports.Begin(
                OperationType.ApplySource,
                targetSource,
                StepNames,
                initialPhase: "fetchPlugins");
            report.SetFinalBaseline(previousBaseline, final: null);

            var prepared = new Dictionary<RepositoryId, LocalGitSourcePreparation>();
            RuntimeSnapshot? runtimeSnapshot = null;
            var runtimeMutationStarted = false;
            var activePhase = "fetchPlugins";

            void SetPhase(string phase)
            {
                activePhase = phase;
                report.SetPhase(phase);
            }

            try
            {
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
                            new ApplySourceFailure(ApplySourceFailureReason.GitRepositoryUnavailable, stepName, repository),
                            previousBaseline,
                            previousStates,
                            prepared,
                            runtimeSnapshot,
                            runtimeMutationStarted);
                    }

                    if (!fetchResult.Succeeded)
                    {
                        return await FailAsync(
                            report,
                            new ApplySourceFailure(ToFailureReason(fetchResult.Status), stepName, repository),
                            previousBaseline,
                            previousStates,
                            prepared,
                            runtimeSnapshot,
                            runtimeMutationStarted);
                    }

                    report.CompleteStep(stepName);
                }

                SetPhase("validateSource");
                (GitCommitSha Plugins, GitCommitSha Configs) targetHeads;
                LocalGitSourcePreparation pluginsPreparation;
                LocalGitSourcePreparation configsPreparation;
                try
                {
                    targetHeads = await ResolveTargetHeadsAsync(targetSource, cancellationToken);
                    pluginsPreparation = await localGit.ValidateSourcePreparationAsync(
                        RepositoryId.Plugins,
                        targetSource,
                        targetHeads.Plugins,
                        cancellationToken);
                    configsPreparation = await localGit.ValidateSourcePreparationAsync(
                        RepositoryId.Configs,
                        targetSource,
                        targetHeads.Configs,
                        cancellationToken);
                }
                catch (ApplyValidationException exception)
                {
                    return await FailAsync(
                        report,
                        exception.Failure,
                        previousBaseline,
                        previousStates,
                        prepared,
                        runtimeSnapshot,
                        runtimeMutationStarted);
                }
                catch (LocalGitException)
                {
                    return await FailAsync(
                        report,
                        new ApplySourceFailure(ApplySourceFailureReason.GitWorkspaceUnsafe, "validateSource"),
                        previousBaseline,
                        previousStates,
                        prepared,
                        runtimeSnapshot,
                        runtimeMutationStarted);
                }

                report.CompleteStep("validateSource");

                foreach (var (repository, preparation) in new[]
                         {
                         (RepositoryId.Plugins, pluginsPreparation),
                         (RepositoryId.Configs, configsPreparation)
                     })
                {
                    var stepName = PrepareStepName(repository);
                    SetPhase(stepName);
                    prepared[repository] = preparation;

                    try
                    {
                        await localGit.PrepareSourceAsync(
                            repository,
                            targetSource,
                            preparation.TargetHead,
                            cancellationToken);
                    }
                    catch (LocalGitException)
                    {
                        return await FailAsync(
                            report,
                            new ApplySourceFailure(ApplySourceFailureReason.SourcePreparationFailed, stepName, repository),
                            previousBaseline,
                            previousStates,
                            prepared,
                            runtimeSnapshot,
                            runtimeMutationStarted);
                    }

                    report.CompleteStep(stepName);
                }

                SetPhase("calculateApply");
                IReadOnlyList<TargetRuntimeFile> targetFiles;
                IReadOnlyList<RuntimeFileChange> runtimeChanges;
                try
                {
                    targetFiles = await GetTargetFilesAsync(targetHeads, cancellationToken);
                    runtimeSnapshot = await CaptureRuntimeSnapshotAsync(cancellationToken);
                    runtimeChanges = CalculateChanges(targetFiles, runtimeSnapshot);
                }
                catch (LocalGitException)
                {
                    return await FailAsync(
                        report,
                        new ApplySourceFailure(ApplySourceFailureReason.SourcePreparationFailed, "calculateApply"),
                        previousBaseline,
                        previousStates,
                        prepared,
                        runtimeSnapshot,
                        runtimeMutationStarted);
                }
                catch (Exception exception) when (IsRuntimeOperationException(exception))
                {
                    return await FailAsync(
                        report,
                        new ApplySourceFailure(
                            ApplySourceFailureReason.RuntimeApplyFailed,
                            "calculateApply"),
                        previousBaseline,
                        previousStates,
                        prepared,
                        runtimeSnapshot,
                        runtimeMutationStarted);
                }

                report.SetChanges(runtimeChanges.Select(ToOperationChange).ToArray());
                report.CompleteStep("calculateApply");

                SetPhase("writeRuntime");
                runtimeMutationStarted = true;
                try
                {
                    await ApplyRuntimeAsync(targetFiles, runtimeSnapshot, runtimeChanges, cancellationToken);
                }
                catch (RuntimeApplyException exception)
                {
                    return await FailAsync(
                        report,
                        new ApplySourceFailure(
                            ApplySourceFailureReason.RuntimeApplyFailed,
                            "writeRuntime",
                            exception.File.Repository,
                            exception.File.RuntimeRelativePath),
                        previousBaseline,
                        previousStates,
                        prepared,
                        runtimeSnapshot,
                        runtimeMutationStarted);
                }

                report.CompleteStep("writeRuntime");

                SetPhase("persistBaseline");
                var newBaseline = new BaselineModel(
                    environment,
                    targetSource,
                    targetHeads.Plugins,
                    targetHeads.Configs,
                    DateTimeOffset.UtcNow);
                try
                {
                    await baselineStore.SaveAsync(newBaseline, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    return await FailAsync(
                        report,
                        new ApplySourceFailure(ApplySourceFailureReason.BaselinePersistFailed, "persistBaseline"),
                        previousBaseline,
                        previousStates,
                        prepared,
                        runtimeSnapshot,
                        runtimeMutationStarted);
                }

                report.CompleteStep("persistBaseline");
                await CaptureFinalStateAsync(report, previousBaseline, CancellationToken.None);
                report.Succeed();
                return new ApplySourceResult(report.Id, Failure: null);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return await FailAsync(
                    report,
                    new ApplySourceFailure(ApplySourceFailureReason.OperationCanceled, activePhase),
                    previousBaseline,
                    previousStates,
                    prepared,
                    runtimeSnapshot,
                    runtimeMutationStarted);
            }
        }
    }

    private async Task<(GitCommitSha Plugins, GitCommitSha Configs)> ResolveTargetHeadsAsync(
        Source targetSource,
        CancellationToken cancellationToken)
    {
        GitCommitSha? pluginsHead;
        GitCommitSha? configsHead;

        try
        {
            pluginsHead = await localGit.GetKnownRemoteSourceHeadAsync(
                RepositoryId.Plugins,
                targetSource,
                cancellationToken);
            configsHead = await localGit.GetKnownRemoteSourceHeadAsync(
                RepositoryId.Configs,
                targetSource,
                cancellationToken);
        }
        catch (LocalGitException)
        {
            throw new ApplyValidationException(
                new ApplySourceFailure(ApplySourceFailureReason.GitRepositoryUnavailable, "validateSource"));
        }

        var missing = new[]
            {
                (Repository: RepositoryId.Plugins, Head: pluginsHead),
                (Repository: RepositoryId.Configs, Head: configsHead)
            }
            .Where(item => item.Head is null)
            .Select(item => item.Repository)
            .ToArray();
        if (missing.Length > 0)
        {
            throw new ApplyValidationException(new ApplySourceFailure(
                targetSource.Kind is SourceKind.Release && missing.Length == 1
                    ? ApplySourceFailureReason.ReleaseIncomplete
                    : ApplySourceFailureReason.TargetSourceUnavailable,
                "validateSource",
                MissingRepositories: missing));
        }

        return (pluginsHead!, configsHead!);
    }

    private async Task<IReadOnlyList<TargetRuntimeFile>> GetTargetFilesAsync(
        (GitCommitSha Plugins, GitCommitSha Configs) targetHeads,
        CancellationToken cancellationToken)
    {
        var targetFiles = new List<TargetRuntimeFile>();

        foreach (var repository in Repositories)
        {
            var commit = repository is RepositoryId.Plugins ? targetHeads.Plugins : targetHeads.Configs;
            var paths = await localGit.ListFilesAtCommitAsync(repository, commit, cancellationToken);

            foreach (var file in paths.Select(path => mapping.FromRepositoryPath(repository, path)).OfType<ManagedFile>())
            {
                var content = await localGit.ReadFileAtCommitAsync(
                    repository,
                    commit,
                    file.RepositoryRelativePath,
                    cancellationToken);
                targetFiles.Add(new TargetRuntimeFile(file, content));
            }
        }

        return targetFiles;
    }

    private async Task<RuntimeSnapshot> CaptureRuntimeSnapshotAsync(CancellationToken cancellationToken)
    {
        var files = await managedRuntimeFiles.ListAsync(cancellationToken);
        var snapshotFiles = new List<RuntimeSnapshotFile>();

        foreach (var file in files)
        {
            snapshotFiles.Add(new RuntimeSnapshotFile(
                file,
                await runtimeFiles.ReadAsync(file, cancellationToken)));
        }

        return new RuntimeSnapshot(snapshotFiles);
    }

    private static IReadOnlyList<RuntimeFileChange> CalculateChanges(
        IReadOnlyList<TargetRuntimeFile> targetFiles,
        RuntimeSnapshot runtimeSnapshot)
    {
        var targetByPath = targetFiles.ToDictionary(file => file.File.RuntimeRelativePath, StringComparer.Ordinal);
        var runtimeByPath = runtimeSnapshot.Files.ToDictionary(file => file.File.RuntimeRelativePath, StringComparer.Ordinal);
        var changes = new List<RuntimeFileChange>();

        foreach (var path in targetByPath.Keys.Concat(runtimeByPath.Keys).Distinct(StringComparer.Ordinal).OrderBy(path => path, StringComparer.Ordinal))
        {
            var targetFile = targetByPath.GetValueOrDefault(path);
            var runtimeFile = runtimeByPath.GetValueOrDefault(path);

            if (runtimeFile is null)
            {
                changes.Add(new RuntimeFileChange(targetFile!.File, RuntimeChangeKind.Added));
            }
            else if (targetFile is null)
            {
                changes.Add(new RuntimeFileChange(runtimeFile.File, RuntimeChangeKind.Deleted));
            }
            else if (!targetFile.Content.AsSpan().SequenceEqual(runtimeFile.Content))
            {
                changes.Add(new RuntimeFileChange(targetFile.File, RuntimeChangeKind.Modified));
            }
        }

        return changes;
    }

    private async Task ApplyRuntimeAsync(
        IReadOnlyList<TargetRuntimeFile> targetFiles,
        RuntimeSnapshot runtimeSnapshot,
        IReadOnlyList<RuntimeFileChange> changes,
        CancellationToken cancellationToken)
    {
        var targetByPath = targetFiles.ToDictionary(file => file.File.RuntimeRelativePath, StringComparer.Ordinal);

        foreach (var change in changes.Where(change => change.Change is RuntimeChangeKind.Added or RuntimeChangeKind.Modified))
        {
            try
            {
                await runtimeFiles.WriteAsync(
                    change.File,
                    targetByPath[change.File.RuntimeRelativePath].Content,
                    cancellationToken);
            }
            catch (Exception exception) when (IsRuntimeOperationException(exception))
            {
                throw new RuntimeApplyException(change.File, exception);
            }
        }

        foreach (var change in changes.Where(change => change.Change is RuntimeChangeKind.Deleted))
        {
            try
            {
                await runtimeFiles.DeleteAsync(change.File, cancellationToken);
            }
            catch (Exception exception) when (IsRuntimeOperationException(exception))
            {
                throw new RuntimeApplyException(change.File, exception);
            }
        }
    }

    private async Task<ApplySourceResult> FailAsync(
        OperationReportTracker report,
        ApplySourceFailure failure,
        BaselineModel? previousBaseline,
        IReadOnlyDictionary<RepositoryId, LocalGitRepositoryState> previousStates,
        IReadOnlyDictionary<RepositoryId, LocalGitSourcePreparation> prepared,
        RuntimeSnapshot? runtimeSnapshot,
        bool runtimeMutationStarted)
    {
        report.FailStep(failure.Phase);

        var rollbackMessages = new List<string>();
        var rollbackSucceeded = true;
        var rollbackAttempted = false;

        if (runtimeMutationStarted && runtimeSnapshot is not null)
        {
            rollbackAttempted = true;
            try
            {
                await RestoreRuntimeAsync(runtimeSnapshot, CancellationToken.None);
                rollbackMessages.Add("Runtime files restored.");
            }
            catch (Exception exception) when (exception is InvalidOperationException or AggregateException)
            {
                rollbackSucceeded = false;
                rollbackMessages.Add($"Runtime rollback failed: {exception.Message}");
            }
        }

        foreach (var repository in Repositories.Reverse())
        {
            if (!prepared.TryGetValue(repository, out var preparation))
            {
                continue;
            }

            rollbackAttempted = true;
            try
            {
                await localGit.RestoreSourcePreparationAsync(
                    repository,
                    previousStates[repository],
                    preparation,
                    CancellationToken.None);
                rollbackMessages.Add($"{repository} workspace restored.");
            }
            catch (LocalGitException exception)
            {
                rollbackSucceeded = false;
                rollbackMessages.Add($"{repository} workspace rollback failed: {exception.Message}");
            }
        }

        report.SetRollback(rollbackAttempted
            ? new RollbackResult(
                rollbackSucceeded ? RollbackStatus.Succeeded : RollbackStatus.Failed,
                string.Join(" ", rollbackMessages))
            : new RollbackResult(RollbackStatus.NotAttempted));
        await CaptureFinalStateAsync(report, previousBaseline, CancellationToken.None);
        report.Fail(new OperationFailure(
            failure.Phase,
            failure.ErrorCode,
            failure.ErrorMessage,
            failure.Repository,
            failure.FilePath));

        return new ApplySourceResult(report.Id, failure);
    }

    private async Task RestoreRuntimeAsync(RuntimeSnapshot snapshot, CancellationToken cancellationToken)
    {
        var errors = new List<Exception>();
        IReadOnlyList<ManagedFile> currentFiles;

        try
        {
            currentFiles = await managedRuntimeFiles.ListAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is RuntimeFilesException or IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException("Runtime files could not be enumerated for rollback.", exception);
        }

        var snapshotByPath = snapshot.Files.ToDictionary(file => file.File.RuntimeRelativePath, StringComparer.Ordinal);
        foreach (var file in currentFiles.Where(file => !snapshotByPath.ContainsKey(file.RuntimeRelativePath)))
        {
            try
            {
                await runtimeFiles.DeleteAsync(file, cancellationToken);
            }
            catch (Exception exception) when (exception is RuntimeFilesException or IOException or UnauthorizedAccessException)
            {
                errors.Add(exception);
            }
        }

        foreach (var file in snapshot.Files)
        {
            try
            {
                await runtimeFiles.WriteAsync(file.File, file.Content, cancellationToken);
            }
            catch (Exception exception) when (exception is RuntimeFilesException or IOException or UnauthorizedAccessException)
            {
                errors.Add(exception);
            }
        }

        if (errors.Count > 0)
        {
            throw new AggregateException("One or more Runtime rollback operations failed.", errors);
        }
    }

    private async Task CaptureFinalStateAsync(
        OperationReportTracker report,
        BaselineModel? previousBaseline,
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
            report.SetFinalBaseline(previousBaseline, await baselineStore.GetAsync(cancellationToken));
        }
        catch (BaselineStateException)
        {
            report.SetFinalBaseline(previousBaseline, final: null);
        }

        report.CompleteStep("captureFinalState");
    }

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

    private static string FetchStepName(RepositoryId repository) =>
        repository switch
        {
            RepositoryId.Plugins => "fetchPlugins",
            RepositoryId.Configs => "fetchConfigs",
            _ => throw new ArgumentOutOfRangeException(nameof(repository), repository, "Unsupported repository.")
        };

    private static string PrepareStepName(RepositoryId repository) =>
        repository switch
        {
            RepositoryId.Plugins => "preparePlugins",
            RepositoryId.Configs => "prepareConfigs",
            _ => throw new ArgumentOutOfRangeException(nameof(repository), repository, "Unsupported repository.")
        };

    private static ApplySourceFailureReason ToFailureReason(RemoteGitFetchStatus status) =>
        status switch
        {
            RemoteGitFetchStatus.WorkspaceMissing or RemoteGitFetchStatus.NotRepository =>
                ApplySourceFailureReason.GitRepositoryUnavailable,
            RemoteGitFetchStatus.RemoteMissing => ApplySourceFailureReason.GitRemoteMissing,
            RemoteGitFetchStatus.Failed => ApplySourceFailureReason.GitRemoteFetchFailed,
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unsupported fetch status.")
        };

    private static bool IsRuntimeOperationException(Exception exception) =>
        exception is RuntimeFilesException or IOException or UnauthorizedAccessException;

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

    private sealed record TargetRuntimeFile(ManagedFile File, byte[] Content);

    private sealed record RuntimeSnapshotFile(ManagedFile File, byte[] Content);

    private sealed record RuntimeSnapshot(IReadOnlyList<RuntimeSnapshotFile> Files);

    private sealed class RuntimeApplyException(ManagedFile file, Exception innerException)
        : Exception("A managed Runtime file could not be applied.", innerException)
    {
        public ManagedFile File { get; } = file;
    }

    private sealed class ApplyValidationException(ApplySourceFailure failure)
        : Exception(failure.ErrorMessage)
    {
        public ApplySourceFailure Failure { get; } = failure;
    }
}
