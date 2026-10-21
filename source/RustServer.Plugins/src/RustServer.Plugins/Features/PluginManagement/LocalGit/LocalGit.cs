using Microsoft.Extensions.Options;
using RustServer.Plugins.Features.PluginManagement.Git;
using RustServer.Plugins.Features.PluginManagement.Models;
using RustServer.Plugins.Features.PluginManagement.Models.Git;

namespace RustServer.Plugins.Features.PluginManagement.LocalGit;

public sealed class LocalGit(IOptions<LocalGitOptions> options, GitProcess gitProcess) : ILocalGit
{
    private readonly LocalGitOptions localGitOptions = options.Value;

    public async Task<IReadOnlySet<string>> GetIgnoredPathsAsync(
        RepositoryId repository,
        IReadOnlyList<string> repositoryRelativePaths,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(repositoryRelativePaths);
        var ignored = new HashSet<string>(StringComparer.Ordinal);
        if (repositoryRelativePaths.Count == 0)
        {
            return ignored;
        }

        // Bounded argv batches avoid per-file Git processes and OS command-line limits.
        // Git itself resolves nested .gitignore, negated patterns, and tracked files.
        const int maxPathsPerCall = 96;
        const int maxPathBytesPerCall = 12_000;
        var workspacePath = localGitOptions.GetWorkspacePath(repository);
        var cursor = 0;
        while (cursor < repositoryRelativePaths.Count)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var batch = new List<string>();
            var bytes = 0;
            var count = 0;
            while (cursor < repositoryRelativePaths.Count)
            {
                var path = repositoryRelativePaths[cursor];
                ValidateRepositoryRelativePath(path);
                var pathBytes = System.Text.Encoding.UTF8.GetByteCount(path) + 1;
                if (count > 0 && (count >= maxPathsPerCall || bytes + pathBytes > maxPathBytesPerCall))
                {
                    break;
                }

                batch.Add(path);
                bytes += pathBytes;
                count++;
                cursor++;
            }

            // Git requires --stdin when -z is used; transmit paths as NUL-delimited input.
            var input = System.Text.Encoding.UTF8.GetBytes(string.Join('\0', batch) + '\0');
            GitCommandResult result;
            try
            {
                result = await gitProcess.ExecuteReadOnlyWithStandardInputAsync(
                    workspacePath, ["check-ignore", "--stdin", "-z"], input, cancellationToken);
            }
            catch (GitProcessException exception)
            {
                throw new LocalGitException("Git ignore rules could not be evaluated.", exception);
            }
            // git check-ignore: 0=at least one ignored path, 1=no ignored paths, other=error.
            if (result.ExitCode is not (0 or 1))
            {
                throw CreateCommandException("read Git ignore rules");
            }

            foreach (var path in ParseNullDelimitedPaths(result.StandardOutput))
            {
                ignored.Add(path);
            }
        }

        return ignored;
    }

    public async Task<LocalGitRepositoryState> GetStateAsync(
        RepositoryId repository,
        CancellationToken cancellationToken)
    {
        var workspacePath = localGitOptions.GetWorkspacePath(repository);

        if (!Directory.Exists(workspacePath))
        {
            return CreateDiagnosticState(
                repository,
                workspacePath,
                LocalGitDiagnostic.WorkspaceMissing);
        }

        var workTreeResult = await ExecuteReadOnlyAsync(
            workspacePath,
            ["rev-parse", "--is-inside-work-tree"],
            cancellationToken);

        if (workTreeResult.ExitCode != 0 ||
            !string.Equals(workTreeResult.StandardOutput.Trim(), "true", StringComparison.Ordinal))
        {
            return CreateDiagnosticState(
                repository,
                workspacePath,
                LocalGitDiagnostic.NotRepository);
        }

        var branchResult = await ExecuteReadOnlyAsync(
            workspacePath,
            ["symbolic-ref", "--quiet", "--short", "HEAD"],
            cancellationToken);
        var branch = branchResult.ExitCode switch
        {
            0 => branchResult.StandardOutput.Trim(),
            1 => null,
            _ => throw CreateCommandException("read the current branch")
        };

        var headResult = await ExecuteReadOnlyAsync(
            workspacePath,
            ["rev-parse", "--verify", "HEAD"],
            cancellationToken);

        if (headResult.ExitCode != 0)
        {
            return CreateState(
                repository,
                workspacePath,
                branch,
                null,
                null,
                null,
                null,
                null,
                LocalGitRelation.Unknown,
                LocalGitDiagnostic.HeadUnavailable);
        }

        var statusResult = await ExecuteReadOnlyAsync(
            workspacePath,
            ["status", "--porcelain=v1", "--untracked-files=all"],
            cancellationToken);
        EnsureSuccess(statusResult, "read the working tree status");

        var diagnostic = branch is null
            ? LocalGitDiagnostic.DetachedHead
            : IsSupportedBranch(branch)
                ? LocalGitDiagnostic.None
                : LocalGitDiagnostic.UnsupportedBranch;
        var head = new GitCommitSha(headResult.StandardOutput.Trim());
        var isClean = string.IsNullOrEmpty(statusResult.StandardOutput);

        if (branch is null)
        {
            return CreateState(
                repository,
                workspacePath,
                branch,
                head,
                isClean,
                null,
                null,
                null,
                LocalGitRelation.Unknown,
                diagnostic);
        }

        var trackingResult = await ExecuteReadOnlyAsync(
            workspacePath,
            ["for-each-ref", "--format=%(upstream:short)", $"refs/heads/{branch}"],
            cancellationToken);
        EnsureSuccess(trackingResult, "read the tracking branch");

        var trackingBranch = trackingResult.StandardOutput.Trim();
        if (string.IsNullOrEmpty(trackingBranch))
        {
            return CreateState(
                repository,
                workspacePath,
                branch,
                head,
                isClean,
                null,
                null,
                null,
                LocalGitRelation.NoTracking,
                diagnostic);
        }

        var relationResult = await ExecuteReadOnlyAsync(
            workspacePath,
            ["rev-list", "--left-right", "--count", "HEAD...@{upstream}"],
            cancellationToken);
        EnsureSuccess(relationResult, "read the ahead/behind relation");

        var (ahead, behind) = ParseRelationCounts(relationResult.StandardOutput);
        return CreateState(
            repository,
            workspacePath,
            branch,
            head,
            isClean,
            trackingBranch,
            ahead,
            behind,
            ToRelation(ahead, behind),
            diagnostic);
    }

    public async Task<LocalGitDevelopmentUpdateResult> TryFastForwardDevelopmentAsync(
        RepositoryId repository,
        CancellationToken cancellationToken)
    {
        var state = await GetStateAsync(repository, cancellationToken);

        if (!CanFastForwardDevelopment(state))
        {
            return new LocalGitDevelopmentUpdateResult(false, state);
        }

        var result = await ExecuteAsync(
            state.WorkspacePath,
            ["merge", "--ff-only", "@{upstream}"],
            cancellationToken);
        EnsureSuccess(result, "fast-forward local Development");

        return new LocalGitDevelopmentUpdateResult(
            true,
            await GetStateAsync(repository, cancellationToken));
    }

    public async Task FastForwardCurrentBranchAsync(
        RepositoryId repository,
        CancellationToken cancellationToken)
    {
        var state = await GetStateAsync(repository, cancellationToken);
        if (!IsSafeSupportedTrackingState(state) || state.Relation is not LocalGitRelation.Behind)
        {
            throw new LocalGitException("The current branch is not safe to fast-forward.");
        }

        EnsureSuccess(
            await ExecuteAsync(state.WorkspacePath, ["merge", "--ff-only", "@{upstream}"], cancellationToken),
            "fast-forward the current branch");

        var finalState = await GetStateAsync(repository, cancellationToken);
        if (!IsSafeSupportedTrackingState(finalState) || finalState.Relation is not LocalGitRelation.Equal)
        {
            throw new LocalGitException("The current branch did not reach its remote-tracking state after fast-forward.");
        }
    }

    public async Task<LocalGitMergeResult> MergeCurrentBranchWithUpstreamAsync(
        RepositoryId repository,
        string authorName,
        string authorEmail,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(authorName);
        ArgumentException.ThrowIfNullOrWhiteSpace(authorEmail);

        var state = await GetStateAsync(repository, cancellationToken);
        if (!IsSafeSupportedTrackingState(state) || state.Relation is not LocalGitRelation.Diverged)
        {
            throw new LocalGitException("The current branch is not safe to merge with its remote-tracking branch.");
        }

        var result = await ExecuteAsync(
            state.WorkspacePath,
            [
                "-c", $"user.name={authorName}",
                "-c", $"user.email={authorEmail}",
                "merge", "--no-edit", "@{upstream}"
            ],
            cancellationToken);
        if (result.ExitCode == 0)
        {
            return new LocalGitMergeResult(LocalGitMergeStatus.Succeeded);
        }

        var mergeHead = await ExecuteReadOnlyAsync(
            state.WorkspacePath,
            ["rev-parse", "--verify", "--quiet", "MERGE_HEAD"],
            CancellationToken.None);
        return new LocalGitMergeResult(
            mergeHead.ExitCode == 0 ? LocalGitMergeStatus.Conflict : LocalGitMergeStatus.Failed);
    }

    public async Task<bool> IsMergeInProgressAsync(
        RepositoryId repository,
        CancellationToken cancellationToken)
    {
        var mergeHead = await ExecuteReadOnlyAsync(
            localGitOptions.GetWorkspacePath(repository),
            ["rev-parse", "--verify", "--quiet", "MERGE_HEAD"],
            cancellationToken);

        return mergeHead.ExitCode switch
        {
            0 => true,
            1 => false,
            _ => throw CreateCommandException("check whether a merge is in progress")
        };
    }

    public async Task AbortMergeAsync(
        RepositoryId repository,
        LocalGitRepositoryState originalState,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(originalState);

        if (originalState.Repository != repository || originalState.Branch is null || originalState.Head is null)
        {
            throw new LocalGitException("The original branch state cannot be used for merge recovery.");
        }

        var workspacePath = localGitOptions.GetWorkspacePath(repository);
        EnsureSuccess(
            await ExecuteAsync(workspacePath, ["merge", "--abort"], cancellationToken),
            "abort the conflicted merge");

        var restoredState = await GetStateAsync(repository, cancellationToken);
        if (restoredState.IsClean is not true ||
            !string.Equals(restoredState.Branch, originalState.Branch, StringComparison.Ordinal) ||
            !string.Equals(restoredState.Head?.Value, originalState.Head.Value, StringComparison.Ordinal))
        {
            throw new LocalGitException("Merge abort did not restore the original branch and HEAD.");
        }
    }

    public async Task<GitCommitSha?> GetKnownRemoteSourceHeadAsync(
        RepositoryId repository,
        Source source,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);

        var remoteTrackingRef = $"refs/remotes/origin/{source.BranchName}";
        var result = await ExecuteReadOnlyAsync(
            localGitOptions.GetWorkspacePath(repository),
            ["show-ref", "--verify", "--hash", remoteTrackingRef],
            cancellationToken);

        return result.ExitCode == 0
            ? new GitCommitSha(result.StandardOutput.Trim())
            : null;
    }

    public async Task<LocalGitSourcePreparation> ValidateSourcePreparationAsync(
        RepositoryId repository,
        Source source,
        GitCommitSha targetHead,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(targetHead);

        var currentState = await GetStateAsync(repository, cancellationToken);
        EnsureWorkspaceIsSafeForSourcePreparation(currentState);

        var remoteHead = await GetKnownRemoteSourceHeadAsync(repository, source, cancellationToken);
        if (remoteHead is null || !string.Equals(remoteHead.Value, targetHead.Value, StringComparison.Ordinal))
        {
            throw new LocalGitException("The target remote-tracking Source is unavailable or changed.");
        }

        var workspacePath = localGitOptions.GetWorkspacePath(repository);
        var localBranchReference = $"refs/heads/{source.BranchName}";
        var branchExists = await ExecuteReadOnlyAsync(
            workspacePath,
            ["show-ref", "--verify", "--quiet", localBranchReference],
            cancellationToken);

        if (branchExists.ExitCode != 0)
        {
            return new LocalGitSourcePreparation(
                repository,
                source,
                targetHead,
                LocalGitSourcePreparationKind.CreateBranch);
        }

        var localHeadResult = await ExecuteReadOnlyAsync(
            workspacePath,
            ["rev-parse", "--verify", localBranchReference],
            cancellationToken);
        EnsureSuccess(localHeadResult, "read the target local branch");

        var trackingResult = await ExecuteReadOnlyAsync(
            workspacePath,
            ["for-each-ref", "--format=%(upstream:short)", localBranchReference],
            cancellationToken);
        EnsureSuccess(trackingResult, "read target branch tracking");
        var expectedTrackingBranch = $"origin/{source.BranchName}";
        if (!string.Equals(trackingResult.StandardOutput.Trim(), expectedTrackingBranch, StringComparison.Ordinal))
        {
            throw new LocalGitException("The target local branch does not track the expected remote branch.");
        }

        var relationResult = await ExecuteReadOnlyAsync(
            workspacePath,
            ["rev-list", "--left-right", "--count", $"{localBranchReference}...{expectedTrackingBranch}"],
            cancellationToken);
        EnsureSuccess(relationResult, "read the target local branch relation");
        var (ahead, behind) = ParseRelationCounts(relationResult.StandardOutput);

        if (ahead > 0)
        {
            throw new LocalGitException("The target local branch is ahead of or diverged from its remote-tracking branch.");
        }

        return new LocalGitSourcePreparation(
            repository,
            source,
            targetHead,
            behind > 0
                ? LocalGitSourcePreparationKind.FastForward
                : LocalGitSourcePreparationKind.ExistingEqual);
    }

    public async Task<LocalGitSourcePreparation> PrepareSourceAsync(
        RepositoryId repository,
        Source source,
        GitCommitSha targetHead,
        CancellationToken cancellationToken)
    {
        var preparation = await ValidateSourcePreparationAsync(
            repository,
            source,
            targetHead,
            cancellationToken);
        var workspacePath = localGitOptions.GetWorkspacePath(repository);

        switch (preparation.Kind)
        {
            case LocalGitSourcePreparationKind.ExistingEqual:
            case LocalGitSourcePreparationKind.FastForward:
                EnsureSuccess(
                    await ExecuteAsync(workspacePath, ["switch", source.BranchName], cancellationToken),
                    "switch to the target Source branch");
                break;
            case LocalGitSourcePreparationKind.CreateBranch:
                EnsureSuccess(
                    await ExecuteAsync(
                        workspacePath,
                        ["switch", "--create", source.BranchName, "--track", $"origin/{source.BranchName}"],
                        cancellationToken),
                    "create and switch to the target Source branch");
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(preparation.Kind),
                    preparation.Kind,
                    "Unsupported Source preparation kind.");
        }

        if (preparation.Kind is LocalGitSourcePreparationKind.FastForward)
        {
            EnsureSuccess(
                await ExecuteAsync(
                    workspacePath,
                    ["merge", "--ff-only", $"origin/{source.BranchName}"],
                    cancellationToken),
                "fast-forward the target Source branch");
        }

        var preparedState = await GetStateAsync(repository, cancellationToken);
        var expectedTrackingBranch = $"origin/{source.BranchName}";
        if (!preparedState.IsRepository ||
            preparedState.IsClean is not true ||
            preparedState.Diagnostic is not LocalGitDiagnostic.None ||
            !string.Equals(preparedState.Branch, source.BranchName, StringComparison.Ordinal) ||
            !string.Equals(preparedState.Head?.Value, targetHead.Value, StringComparison.Ordinal) ||
            !string.Equals(preparedState.TrackingBranch, expectedTrackingBranch, StringComparison.Ordinal) ||
            preparedState.Relation is not LocalGitRelation.Equal)
        {
            throw new LocalGitException("The target Source workspace did not reach the validated state.");
        }

        return preparation;
    }

    public async Task<LocalGitSourcePreparation> PrepareSourceWithoutSynchronizationAsync(
        RepositoryId repository,
        Source source,
        GitCommitSha targetHead,
        CancellationToken cancellationToken)
    {
        var preparation = await ValidateSourcePreparationAsync(
            repository,
            source,
            targetHead,
            cancellationToken);
        if (preparation.Kind is LocalGitSourcePreparationKind.FastForward)
        {
            throw new LocalGitException("The target Source branch requires a separate Git Sync operation.");
        }

        var workspacePath = localGitOptions.GetWorkspacePath(repository);
        switch (preparation.Kind)
        {
            case LocalGitSourcePreparationKind.ExistingEqual:
                EnsureSuccess(
                    await ExecuteAsync(workspacePath, ["switch", source.BranchName], cancellationToken),
                    "switch to the target Source branch");
                break;
            case LocalGitSourcePreparationKind.CreateBranch:
                EnsureSuccess(
                    await ExecuteAsync(
                        workspacePath,
                        ["switch", "--create", source.BranchName, "--track", $"origin/{source.BranchName}"],
                        cancellationToken),
                    "create and switch to the target Source branch");
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(preparation.Kind),
                    preparation.Kind,
                    "Unsupported Source preparation kind.");
        }

        var preparedState = await GetStateAsync(repository, cancellationToken);
        var expectedTrackingBranch = $"origin/{source.BranchName}";
        if (!preparedState.IsRepository ||
            preparedState.IsClean is not true ||
            preparedState.Diagnostic is not LocalGitDiagnostic.None ||
            !string.Equals(preparedState.Branch, source.BranchName, StringComparison.Ordinal) ||
            !string.Equals(preparedState.Head?.Value, targetHead.Value, StringComparison.Ordinal) ||
            !string.Equals(preparedState.TrackingBranch, expectedTrackingBranch, StringComparison.Ordinal) ||
            preparedState.Relation is not LocalGitRelation.Equal)
        {
            throw new LocalGitException("The target Source workspace did not reach the validated state.");
        }

        return preparation;
    }

    public async Task RestoreSourcePreparationAsync(
        RepositoryId repository,
        LocalGitRepositoryState previousState,
        LocalGitSourcePreparation preparation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(previousState);
        ArgumentNullException.ThrowIfNull(preparation);

        if (previousState.Repository != repository || preparation.Repository != repository ||
            previousState.Branch is null || previousState.Head is null)
        {
            throw new LocalGitException("The previous local workspace state cannot be restored safely.");
        }

        var currentState = await GetStateAsync(repository, cancellationToken);
        if (currentState.IsClean is not true)
        {
            throw new LocalGitException("The changed workspace cannot be restored while it is dirty.");
        }

        var workspacePath = localGitOptions.GetWorkspacePath(repository);
        EnsureSuccess(
            await ExecuteAsync(workspacePath, ["switch", "--detach"], cancellationToken),
            "detach the workspace for rollback");

        if (preparation.Kind is LocalGitSourcePreparationKind.CreateBranch &&
            !string.Equals(preparation.Source.BranchName, previousState.Branch, StringComparison.Ordinal))
        {
            var targetBranchReference = $"refs/heads/{preparation.Source.BranchName}";
            var targetBranchExists = await ExecuteReadOnlyAsync(
                workspacePath,
                ["show-ref", "--verify", "--quiet", targetBranchReference],
                cancellationToken);
            if (targetBranchExists.ExitCode == 0)
            {
                EnsureSuccess(
                    await ExecuteAsync(
                        workspacePath,
                        ["update-ref", "-d", targetBranchReference, preparation.TargetHead.Value],
                        cancellationToken),
                    "remove the operation-created target branch");
            }
        }

        EnsureSuccess(
            await ExecuteAsync(
                workspacePath,
                ["update-ref", $"refs/heads/{previousState.Branch}", previousState.Head.Value],
                cancellationToken),
            "restore the previous local branch reference");
        EnsureSuccess(
            await ExecuteAsync(workspacePath, ["switch", previousState.Branch], cancellationToken),
            "restore the previous local branch");

        var restoredState = await GetStateAsync(repository, cancellationToken);
        if (!string.Equals(restoredState.Branch, previousState.Branch, StringComparison.Ordinal) ||
            !string.Equals(restoredState.Head?.Value, previousState.Head.Value, StringComparison.Ordinal) ||
            restoredState.IsClean is not true)
        {
            throw new LocalGitException("The local workspace did not return to its previous state.");
        }
    }

    public async Task<IReadOnlyList<string>> ListFilesAtCommitAsync(
        RepositoryId repository,
        GitCommitSha commit,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(commit);

        var result = await ExecuteReadOnlyAsync(
            localGitOptions.GetWorkspacePath(repository),
            ["ls-tree", "-r", "--name-only", commit.Value],
            cancellationToken);
        EnsureSuccess(result, "list files at the baseline commit");

        return result.StandardOutput
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(path => path.TrimEnd('\r'))
            .ToArray();
    }

    public async Task<byte[]> ReadFileAtCommitAsync(
        RepositoryId repository,
        GitCommitSha commit,
        string repositoryRelativePath,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(commit);

        if (string.IsNullOrWhiteSpace(repositoryRelativePath) ||
            Path.IsPathRooted(repositoryRelativePath) ||
            repositoryRelativePath.Contains('\\') ||
            repositoryRelativePath.Contains("..", StringComparison.Ordinal))
        {
            throw new ArgumentException("Repository path must be a safe relative path.", nameof(repositoryRelativePath));
        }

        var result = await ExecuteReadOnlyForBytesAsync(
            localGitOptions.GetWorkspacePath(repository),
            ["show", "--no-textconv", $"{commit.Value}:{repositoryRelativePath}"],
            cancellationToken);
        if (result.ExitCode != 0)
        {
            throw CreateCommandException("read a file at the baseline commit");
        }

        return result.StandardOutput;
    }

    public async Task WriteWorkingFileAsync(
        RepositoryId repository,
        string repositoryRelativePath,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken)
    {
        try
        {
            var path = GetSafeWorkspaceFilePath(repository, repositoryRelativePath, createParentDirectories: true);
            await File.WriteAllBytesAsync(path, content, cancellationToken);
        }
        catch (LocalGitException)
        {
            throw;
        }
        catch (IOException exception)
        {
            throw new LocalGitException("The selected workspace file could not be written.", exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new LocalGitException("The selected workspace file could not be written.", exception);
        }
    }

    public async Task RemoveWorkingFileAsync(
        RepositoryId repository,
        string repositoryRelativePath,
        CancellationToken cancellationToken)
    {
        ValidateRepositoryRelativePath(repositoryRelativePath);
        var result = await ExecuteAsync(
            localGitOptions.GetWorkspacePath(repository),
            ["rm", "--", repositoryRelativePath],
            cancellationToken);
        EnsureSuccess(result, "remove a selected workspace file");
    }

    public async Task StageWorkingFileAsync(
        RepositoryId repository,
        string repositoryRelativePath,
        CancellationToken cancellationToken)
    {
        ValidateRepositoryRelativePath(repositoryRelativePath);
        var result = await ExecuteAsync(
            localGitOptions.GetWorkspacePath(repository),
            ["add", "--", repositoryRelativePath],
            cancellationToken);
        EnsureSuccess(result, "stage a selected workspace file");
    }

    public async Task<LocalGitStagingInspection> InspectStagingAsync(
        RepositoryId repository,
        CancellationToken cancellationToken)
    {
        var workspacePath = localGitOptions.GetWorkspacePath(repository);
        var staged = await ExecuteReadOnlyAsync(
            workspacePath,
            ["diff", "--cached", "--no-renames", "--name-only", "-z"],
            cancellationToken);
        var unstaged = await ExecuteReadOnlyAsync(
            workspacePath,
            ["diff", "--no-renames", "--name-only", "-z"],
            cancellationToken);
        var untracked = await ExecuteReadOnlyAsync(
            workspacePath,
            ["ls-files", "--others", "--exclude-standard", "-z"],
            cancellationToken);
        EnsureSuccess(staged, "inspect staged workspace files");
        EnsureSuccess(unstaged, "inspect unstaged workspace files");
        EnsureSuccess(untracked, "inspect untracked workspace files");

        return new LocalGitStagingInspection(
            ParseNullDelimitedPaths(staged.StandardOutput),
            ParseNullDelimitedPaths(unstaged.StandardOutput),
            ParseNullDelimitedPaths(untracked.StandardOutput));
    }

    public async Task<GitCommitSha> CreateCommitAsync(
        RepositoryId repository,
        LocalGitCommit commit,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(commit);
        ArgumentException.ThrowIfNullOrWhiteSpace(commit.Message);
        ArgumentException.ThrowIfNullOrWhiteSpace(commit.AuthorName);
        ArgumentException.ThrowIfNullOrWhiteSpace(commit.AuthorEmail);

        var workspacePath = localGitOptions.GetWorkspacePath(repository);
        var commitResult = await ExecuteAsync(
            workspacePath,
            [
                "-c", $"user.name={commit.AuthorName}",
                "-c", $"user.email={commit.AuthorEmail}",
                "commit", "-m", commit.Message
            ],
            cancellationToken);
        EnsureSuccess(commitResult, "create a commit for selected Runtime changes");

        var headResult = await ExecuteReadOnlyAsync(
            workspacePath,
            ["rev-parse", "--verify", "HEAD"],
            cancellationToken);
        EnsureSuccess(headResult, "read the created commit");
        return new GitCommitSha(headResult.StandardOutput.Trim());
    }

    public async Task RestoreOperationChangesAsync(
        RepositoryId repository,
        IReadOnlyList<LocalGitWorkingChange> changes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(changes);

        var workspacePath = localGitOptions.GetWorkspacePath(repository);
        foreach (var change in changes)
        {
            ValidateRepositoryRelativePath(change.RepositoryRelativePath);
            var result = change.Kind switch
            {
                LocalGitWorkingChangeKind.Added => await ExecuteAsync(
                    workspacePath,
                    ["rm", "--force", "--ignore-unmatch", "--", change.RepositoryRelativePath],
                    cancellationToken),
                LocalGitWorkingChangeKind.Modified or LocalGitWorkingChangeKind.Deleted => await ExecuteAsync(
                    workspacePath,
                    ["restore", "--source=HEAD", "--staged", "--worktree", "--", change.RepositoryRelativePath],
                    cancellationToken),
                _ => throw new ArgumentOutOfRangeException(nameof(change.Kind), change.Kind, "Unsupported workspace change kind.")
            };
            EnsureSuccess(result, "restore an operation-created workspace change");
        }

        var state = await GetStateAsync(repository, cancellationToken);
        if (state.IsClean is not true)
        {
            throw new LocalGitException("The workspace did not return to a clean state after rollback.");
        }
    }

    public async Task RestoreCreatedCommitAsync(
        RepositoryId repository,
        Source source,
        GitCommitSha previousHead,
        GitCommitSha createdCommit,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(previousHead);
        ArgumentNullException.ThrowIfNull(createdCommit);

        var state = await GetStateAsync(repository, cancellationToken);
        if (!state.IsRepository ||
            state.IsClean is not true ||
            !string.Equals(state.Branch, source.BranchName, StringComparison.Ordinal) ||
            !string.Equals(state.Head?.Value, createdCommit.Value, StringComparison.Ordinal))
        {
            throw new LocalGitException("The operation-created commit cannot be rolled back safely.");
        }

        var workspacePath = localGitOptions.GetWorkspacePath(repository);
        EnsureSuccess(
            await ExecuteAsync(workspacePath, ["switch", "--detach"], cancellationToken),
            "detach the workspace for commit rollback");
        EnsureSuccess(
            await ExecuteAsync(
                workspacePath,
                ["update-ref", $"refs/heads/{source.BranchName}", previousHead.Value, createdCommit.Value],
                cancellationToken),
            "remove the operation-created local commit");
        EnsureSuccess(
            await ExecuteAsync(workspacePath, ["switch", source.BranchName], cancellationToken),
            "restore the Source branch after commit rollback");

        var restoredState = await GetStateAsync(repository, cancellationToken);
        if (restoredState.IsClean is not true ||
            !string.Equals(restoredState.Branch, source.BranchName, StringComparison.Ordinal) ||
            !string.Equals(restoredState.Head?.Value, previousHead.Value, StringComparison.Ordinal))
        {
            throw new LocalGitException("The workspace did not return to the previous commit after rollback.");
        }
    }

    private static LocalGitRepositoryState CreateDiagnosticState(
        RepositoryId repository,
        string workspacePath,
        LocalGitDiagnostic diagnostic) =>
        CreateState(
            repository,
            workspacePath,
            null,
            null,
            null,
            null,
            null,
            null,
            LocalGitRelation.Unknown,
            diagnostic);

    private static LocalGitRepositoryState CreateState(
        RepositoryId repository,
        string workspacePath,
        string? branch,
        GitCommitSha? head,
        bool? isClean,
        string? trackingBranch,
        int? ahead,
        int? behind,
        LocalGitRelation relation,
        LocalGitDiagnostic diagnostic) =>
        new(
            repository,
            workspacePath,
            diagnostic is not LocalGitDiagnostic.WorkspaceMissing and not LocalGitDiagnostic.NotRepository,
            branch,
            head,
            isClean,
            trackingBranch,
            ahead,
            behind,
            relation,
            diagnostic);

    private static bool IsSupportedBranch(string branch) =>
        string.Equals(branch, Source.DevelopmentBranchName, StringComparison.Ordinal) ||
        (branch.StartsWith(Release.BranchPrefix, StringComparison.Ordinal) &&
         branch.Length > Release.BranchPrefix.Length);

    private static bool CanFastForwardDevelopment(LocalGitRepositoryState state) =>
        state.IsRepository &&
        string.Equals(state.Branch, Source.DevelopmentBranchName, StringComparison.Ordinal) &&
        state.IsClean is true &&
        string.Equals(state.TrackingBranch, "origin/develop", StringComparison.Ordinal) &&
        state.Ahead == 0 &&
        state.Behind > 0 &&
        state.Relation is LocalGitRelation.Behind &&
        state.Diagnostic is LocalGitDiagnostic.None;

    private static bool IsSafeSupportedTrackingState(LocalGitRepositoryState state) =>
        state.IsRepository &&
        state.IsClean is true &&
        state.Diagnostic is LocalGitDiagnostic.None &&
        state.Branch is not null &&
        IsSupportedBranch(state.Branch) &&
        string.Equals(state.TrackingBranch, $"origin/{state.Branch}", StringComparison.Ordinal) &&
        state.Relation is LocalGitRelation.Equal or LocalGitRelation.Ahead or LocalGitRelation.Behind or LocalGitRelation.Diverged;

    private static void EnsureWorkspaceIsSafeForSourcePreparation(LocalGitRepositoryState state)
    {
        if (!state.IsRepository ||
            state.IsClean is not true ||
            state.Diagnostic is not LocalGitDiagnostic.None ||
            state.Branch is null ||
            state.Head is null ||
            !string.Equals(state.TrackingBranch, $"origin/{state.Branch}", StringComparison.Ordinal) ||
            state.Relation is LocalGitRelation.NoTracking or LocalGitRelation.Ahead or LocalGitRelation.Diverged or LocalGitRelation.Unknown)
        {
            throw new LocalGitException("The local workspace is not safe for Source preparation.");
        }
    }

    private static LocalGitRelation ToRelation(int ahead, int behind)
    {
        if (ahead == 0 && behind == 0)
        {
            return LocalGitRelation.Equal;
        }

        if (ahead > 0)
        {
            return behind > 0 ? LocalGitRelation.Diverged : LocalGitRelation.Ahead;
        }

        return LocalGitRelation.Behind;
    }

    private static (int Ahead, int Behind) ParseRelationCounts(string output)
    {
        var counts = output.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

        if (counts.Length != 2 ||
            !int.TryParse(counts[0], out var ahead) || ahead < 0 ||
            !int.TryParse(counts[1], out var behind) || behind < 0)
        {
            throw new LocalGitException("Git returned an invalid ahead/behind relation.");
        }

        return (ahead, behind);
    }

    private string GetSafeWorkspaceFilePath(
        RepositoryId repository,
        string repositoryRelativePath,
        bool createParentDirectories)
    {
        ValidateRepositoryRelativePath(repositoryRelativePath);

        var workspacePath = Path.GetFullPath(localGitOptions.GetWorkspacePath(repository));
        EnsureNotReparsePoint(workspacePath);
        var segments = repositoryRelativePath.Split('/');
        var directoryPath = workspacePath;

        foreach (var segment in segments[..^1])
        {
            directoryPath = Path.Combine(directoryPath, segment);
            if (Directory.Exists(directoryPath))
            {
                EnsureNotReparsePoint(directoryPath);
                continue;
            }

            if (!createParentDirectories)
            {
                break;
            }

            Directory.CreateDirectory(directoryPath);
            EnsureNotReparsePoint(directoryPath);
        }

        var filePath = Path.GetFullPath(Path.Combine(directoryPath, segments[^1]));
        var workspacePrefix = workspacePath.EndsWith(Path.DirectorySeparatorChar)
            ? workspacePath
            : workspacePath + Path.DirectorySeparatorChar;
        if (!filePath.StartsWith(workspacePrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new LocalGitException("The selected workspace path leaves the repository root.");
        }

        if (File.Exists(filePath))
        {
            EnsureNotReparsePoint(filePath);
        }

        return filePath;
    }

    private static void EnsureNotReparsePoint(string path)
    {
        if (File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))
        {
            throw new LocalGitException("The selected workspace path crosses a reparse point.");
        }
    }

    private static IReadOnlyList<string> ParseNullDelimitedPaths(string output) =>
        output.Split('\0', StringSplitOptions.RemoveEmptyEntries).ToArray();

    private static void ValidateRepositoryRelativePath(string repositoryRelativePath)
    {
        if (string.IsNullOrWhiteSpace(repositoryRelativePath) ||
            Path.IsPathRooted(repositoryRelativePath) ||
            repositoryRelativePath.Contains('\\') ||
            repositoryRelativePath.Contains("..", StringComparison.Ordinal))
        {
            throw new ArgumentException("Repository path must be a safe relative path.", nameof(repositoryRelativePath));
        }
    }

    private async Task<GitCommandResult> ExecuteReadOnlyAsync(
        string workspacePath,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        try
        {
            return await gitProcess.ExecuteReadOnlyAsync(workspacePath, arguments, cancellationToken);
        }
        catch (GitProcessException exception)
        {
            throw new LocalGitException("Git is unavailable while reading the local workspace.", exception);
        }
    }

    private async Task<GitCommandResult> ExecuteAsync(
        string workspacePath,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        try
        {
            return await gitProcess.ExecuteAsync(workspacePath, arguments, cancellationToken);
        }
        catch (GitProcessException exception)
        {
            throw new LocalGitException("Git is unavailable while updating local Development.", exception);
        }
    }

    private async Task<GitBinaryCommandResult> ExecuteReadOnlyForBytesAsync(
        string workspacePath,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        try
        {
            return await gitProcess.ExecuteReadOnlyForBytesAsync(workspacePath, arguments, cancellationToken);
        }
        catch (GitProcessException exception)
        {
            throw new LocalGitException("Git is unavailable while reading a local baseline commit.", exception);
        }
    }

    private static void EnsureSuccess(GitCommandResult result, string operation)
    {
        if (result.ExitCode != 0)
        {
            throw CreateCommandException(operation);
        }
    }

    private static LocalGitException CreateCommandException(string operation) =>
        new($"Git could not {operation}.");
}
