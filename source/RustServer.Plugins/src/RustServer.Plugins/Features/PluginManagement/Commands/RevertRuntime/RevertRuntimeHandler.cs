using RustServer.Plugins.Features.PluginManagement.Baseline;
using RustServer.Plugins.Features.PluginManagement.Guards;
using RustServer.Plugins.Features.PluginManagement.LocalGit;
using RustServer.Plugins.Features.PluginManagement.Models.Git;
using RustServer.Plugins.Features.PluginManagement.Models.Operations;
using RustServer.Plugins.Features.PluginManagement.Operations;
using RustServer.Plugins.Features.PluginManagement.Runtime;
using RustServer.Plugins.Features.PluginManagement.RuntimeChanges;
using BaselineModel = RustServer.Plugins.Features.PluginManagement.Models.Baseline;

namespace RustServer.Plugins.Features.PluginManagement.Commands.RevertRuntime;

public sealed class RevertRuntimeHandler(
    IExclusiveMutationGuard mutationGuard,
    IBaselineStore baselineStore,
    ILocalGit localGit,
    IRuntimeFiles runtimeFiles,
    ManagedRuntimeFileReader managedRuntimeFiles,
    ManagedFileMapping mapping,
    IOperationReports operationReports)
{
    private static readonly RepositoryId[] Repositories = [RepositoryId.Plugins, RepositoryId.Configs];

    private static readonly string[] StepNames =
    [
        "loadBaseline",
        "calculateRevert",
        "restoreRuntime",
        "captureFinalState"
    ];

    public Task<RevertRuntimeResult> HandleAsync(CancellationToken cancellationToken) =>
        HandleAsync(RevertRuntimeCommand.All, cancellationToken);

    public async Task<RevertRuntimeResult> HandleAsync(
        RevertRuntimeCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var lease = mutationGuard.TryAcquire();
        if (lease is null)
        {
            return new RevertRuntimeResult(
                OperationId: null,
                Failure: null,
                new RevertRuntimeRejection(RevertRuntimeRejectionReason.OperationInProgress));
        }

        await using (lease)
        {

            var baseline = await baselineStore.GetAsync(cancellationToken);
            if (baseline is null)
            {
                return new RevertRuntimeResult(
                    OperationId: null,
                    Failure: null,
                    new RevertRuntimeRejection(RevertRuntimeRejectionReason.BaselineNotInitialized));
            }

            var report = operationReports.Begin(
                OperationType.RevertRuntime,
                baseline.Source,
                StepNames,
                initialPhase: "loadBaseline");
            report.SetFinalBaseline(baseline, baseline);

            RuntimeSnapshot? runtimeSnapshot = null;
            var runtimeMutationStarted = false;
            var activePhase = "loadBaseline";

            void SetPhase(string phase)
            {
                activePhase = phase;
                report.SetPhase(phase);
            }

            try
            {
                report.CompleteStep("loadBaseline");

                SetPhase("calculateRevert");
                IReadOnlyList<BaselineRuntimeFile> baselineFiles;
                IReadOnlyList<RuntimeFileChange> changes;
                try
                {
                    var availability = await runtimeFiles.GetAvailabilityAsync(cancellationToken);
                    if (!availability.IsAvailable)
                    {
                        return await FailAsync(
                            report,
                            new RevertRuntimeFailure(RevertRuntimeFailureReason.RuntimeUnavailable, "calculateRevert"),
                            baseline,
                            runtimeSnapshot,
                            runtimeMutationStarted);
                    }

                    baselineFiles = await ReadBaselineFilesAsync(baseline, cancellationToken);
                    runtimeSnapshot = await CaptureRuntimeSnapshotAsync(cancellationToken);
                    changes = CalculateChanges(baselineFiles, runtimeSnapshot);

                    try
                    {
                        changes = ResolveSelectedChanges(changes, command.Selections);
                        runtimeSnapshot = LimitSnapshotToChanges(runtimeSnapshot, changes, command.Selections is not null);
                    }
                    catch (InvalidSelectionException exception)
                    {
                        return await FailAsync(
                            report,
                            new RevertRuntimeFailure(
                                RevertRuntimeFailureReason.InvalidRuntimeSelection,
                                "calculateRevert",
                                exception.Repository,
                                exception.Path),
                            baseline,
                            runtimeSnapshot,
                            runtimeMutationStarted);
                    }
                }
                catch (LocalGitException)
                {
                    return await FailAsync(
                        report,
                        new RevertRuntimeFailure(
                            RevertRuntimeFailureReason.BaselineGitObjectUnavailable,
                            "calculateRevert"),
                        baseline,
                        runtimeSnapshot,
                        runtimeMutationStarted);
                }
                catch (Exception exception) when (IsRuntimeOperationException(exception))
                {
                    return await FailAsync(
                        report,
                        new RevertRuntimeFailure(RevertRuntimeFailureReason.RuntimeUnavailable, "calculateRevert"),
                        baseline,
                        runtimeSnapshot,
                        runtimeMutationStarted);
                }

                report.SetChanges(changes.Select(ToOperationChange).ToArray());
                report.CompleteStep("calculateRevert");

                SetPhase("restoreRuntime");
                runtimeMutationStarted = changes.Count > 0;
                try
                {
                    await RestoreBaselineRuntimeAsync(baselineFiles, changes, cancellationToken);
                }
                catch (RuntimeRevertException exception)
                {
                    return await FailAsync(
                        report,
                        new RevertRuntimeFailure(
                            RevertRuntimeFailureReason.RuntimeRevertFailed,
                            "restoreRuntime",
                            exception.File.Repository,
                            exception.File.RuntimeRelativePath),
                        baseline,
                        runtimeSnapshot,
                        runtimeMutationStarted);
                }

                report.CompleteStep("restoreRuntime");
                await CaptureFinalStateAsync(report, baseline, CancellationToken.None);
                report.Succeed();
                return new RevertRuntimeResult(report.Id, Failure: null);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return await FailAsync(
                    report,
                    new RevertRuntimeFailure(RevertRuntimeFailureReason.OperationCanceled, activePhase),
                    baseline,
                    runtimeSnapshot,
                    runtimeMutationStarted);
            }
        }
    }

    private async Task<IReadOnlyList<BaselineRuntimeFile>> ReadBaselineFilesAsync(
        BaselineModel baseline,
        CancellationToken cancellationToken)
    {
        var baselineFiles = new List<BaselineRuntimeFile>();

        foreach (var repository in Repositories)
        {
            var commit = repository is RepositoryId.Plugins ? baseline.PluginsCommit : baseline.ConfigsCommit;
            var paths = await localGit.ListFilesAtCommitAsync(repository, commit, cancellationToken);

            foreach (var file in paths.Select(path => mapping.FromRepositoryPath(repository, path)).OfType<ManagedFile>())
            {
                var content = await localGit.ReadFileAtCommitAsync(
                    repository,
                    commit,
                    file.RepositoryRelativePath,
                    cancellationToken);
                baselineFiles.Add(new BaselineRuntimeFile(file, content));
            }
        }

        return baselineFiles;
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
        IReadOnlyList<BaselineRuntimeFile> baselineFiles,
        RuntimeSnapshot runtimeSnapshot)
    {
        var baselineByPath = baselineFiles.ToDictionary(file => file.File.RuntimeRelativePath, StringComparer.Ordinal);
        var runtimeByPath = runtimeSnapshot.Files.ToDictionary(file => file.File.RuntimeRelativePath, StringComparer.Ordinal);
        var changes = new List<RuntimeFileChange>();

        foreach (var path in baselineByPath.Keys.Concat(runtimeByPath.Keys).Distinct(StringComparer.Ordinal).OrderBy(path => path, StringComparer.Ordinal))
        {
            var baselineFile = baselineByPath.GetValueOrDefault(path);
            var runtimeFile = runtimeByPath.GetValueOrDefault(path);

            if (baselineFile is null)
            {
                changes.Add(new RuntimeFileChange(runtimeFile!.File, RuntimeChangeKind.Added));
            }
            else if (runtimeFile is null)
            {
                changes.Add(new RuntimeFileChange(baselineFile.File, RuntimeChangeKind.Deleted));
            }
            else if (!baselineFile.Content.AsSpan().SequenceEqual(runtimeFile.Content))
            {
                changes.Add(new RuntimeFileChange(baselineFile.File, RuntimeChangeKind.Modified));
            }
        }

        return changes;
    }

    private static IReadOnlyList<RuntimeFileChange> ResolveSelectedChanges(
        IReadOnlyList<RuntimeFileChange> changes,
        IReadOnlyList<RevertRuntimeSelection>? selections)
    {
        if (selections is null)
        {
            return changes;
        }

        if (selections.Count == 0)
        {
            throw new InvalidSelectionException();
        }

        var changesByFile = changes.ToDictionary(
            change => (change.File.Repository, change.File.RuntimeRelativePath));
        var selectedChanges = new List<RuntimeFileChange>(selections.Count);
        var seen = new HashSet<(RepositoryId Repository, string Path)>();

        foreach (var selection in selections)
        {
            var key = (selection.File.Repository, selection.File.RuntimeRelativePath);
            if (!seen.Add(key) || !changesByFile.TryGetValue(key, out var change))
            {
                throw new InvalidSelectionException(selection.File.Repository, selection.File.RuntimeRelativePath);
            }

            selectedChanges.Add(change);
        }

        return selectedChanges;
    }

    private static RuntimeSnapshot LimitSnapshotToChanges(
        RuntimeSnapshot snapshot,
        IReadOnlyList<RuntimeFileChange> changes,
        bool isSelective)
    {
        if (!isSelective)
        {
            return snapshot;
        }

        var selectedPaths = changes
            .Select(change => change.File.RuntimeRelativePath)
            .ToHashSet(StringComparer.Ordinal);
        return new RuntimeSnapshot(
            snapshot.Files.Where(file => selectedPaths.Contains(file.File.RuntimeRelativePath)).ToArray(),
            changes.Select(change => change.File).ToArray());
    }

    private async Task RestoreBaselineRuntimeAsync(
        IReadOnlyList<BaselineRuntimeFile> baselineFiles,
        IReadOnlyList<RuntimeFileChange> changes,
        CancellationToken cancellationToken)
    {
        var baselineByPath = baselineFiles.ToDictionary(file => file.File.RuntimeRelativePath, StringComparer.Ordinal);

        foreach (var change in changes.Where(change => change.Change is RuntimeChangeKind.Modified or RuntimeChangeKind.Deleted))
        {
            try
            {
                await runtimeFiles.WriteAsync(
                    change.File,
                    baselineByPath[change.File.RuntimeRelativePath].Content,
                    cancellationToken);
            }
            catch (Exception exception) when (IsRuntimeOperationException(exception))
            {
                throw new RuntimeRevertException(change.File, exception);
            }
        }

        foreach (var change in changes.Where(change => change.Change is RuntimeChangeKind.Added))
        {
            try
            {
                await runtimeFiles.DeleteAsync(change.File, cancellationToken);
            }
            catch (Exception exception) when (IsRuntimeOperationException(exception))
            {
                throw new RuntimeRevertException(change.File, exception);
            }
        }
    }

    private async Task<RevertRuntimeResult> FailAsync(
        OperationReportTracker report,
        RevertRuntimeFailure failure,
        BaselineModel baseline,
        RuntimeSnapshot? runtimeSnapshot,
        bool runtimeMutationStarted)
    {
        report.FailStep(failure.Phase);

        if (runtimeMutationStarted && runtimeSnapshot is not null)
        {
            try
            {
                await RestoreRuntimeSnapshotAsync(runtimeSnapshot, CancellationToken.None);
                report.SetRollback(new RollbackResult(RollbackStatus.Succeeded, "Runtime files restored."));
            }
            catch (Exception exception) when (IsRuntimeOperationException(exception) || exception is AggregateException)
            {
                report.SetRollback(new RollbackResult(
                    RollbackStatus.Failed,
                    $"Runtime rollback failed: {exception.Message}"));
            }
        }
        else
        {
            report.SetRollback(new RollbackResult(RollbackStatus.NotAttempted));
        }

        await CaptureFinalStateAsync(report, baseline, CancellationToken.None);
        report.Fail(new OperationFailure(
            failure.Phase,
            failure.ErrorCode,
            failure.ErrorMessage,
            failure.Repository,
            failure.FilePath));

        return new RevertRuntimeResult(report.Id, failure);
    }

    private async Task RestoreRuntimeSnapshotAsync(RuntimeSnapshot snapshot, CancellationToken cancellationToken)
    {
        var errors = new List<Exception>();
        IReadOnlyList<ManagedFile> currentFiles;

        try
        {
            currentFiles = await managedRuntimeFiles.ListAsync(cancellationToken);
        }
        catch (Exception exception) when (IsRuntimeOperationException(exception))
        {
            throw new RuntimeFilesException(
                RuntimeAvailabilityStatus.UnsafePath,
                "Runtime files could not be enumerated for rollback.",
                exception);
        }

        var snapshotByPath = snapshot.Files.ToDictionary(file => file.File.RuntimeRelativePath, StringComparer.Ordinal);
        var scopedPaths = snapshot.Scope is null
            ? null
            : snapshot.Scope.Select(file => file.RuntimeRelativePath).ToHashSet(StringComparer.Ordinal);
        foreach (var file in currentFiles.Where(file =>
                     (scopedPaths is null || scopedPaths.Contains(file.RuntimeRelativePath)) &&
                     !snapshotByPath.ContainsKey(file.RuntimeRelativePath)))
        {
            try
            {
                await runtimeFiles.DeleteAsync(file, cancellationToken);
            }
            catch (Exception exception) when (IsRuntimeOperationException(exception))
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
            catch (Exception exception) when (IsRuntimeOperationException(exception))
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
        BaselineModel previousBaseline,
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

    private sealed record BaselineRuntimeFile(ManagedFile File, byte[] Content);

    private sealed record RuntimeSnapshotFile(ManagedFile File, byte[] Content);

    private sealed record RuntimeSnapshot(
        IReadOnlyList<RuntimeSnapshotFile> Files,
        IReadOnlyList<ManagedFile>? Scope = null);

    private sealed class InvalidSelectionException(RepositoryId? repository = null, string? path = null)
        : Exception("The selected Runtime changes are invalid.")
    {
        public RepositoryId? Repository { get; } = repository;

        public string? Path { get; } = path;
    }

    private sealed class RuntimeRevertException(ManagedFile file, Exception innerException)
        : Exception("A managed Runtime file could not be restored.", innerException)
    {
        public ManagedFile File { get; } = file;
    }
}
