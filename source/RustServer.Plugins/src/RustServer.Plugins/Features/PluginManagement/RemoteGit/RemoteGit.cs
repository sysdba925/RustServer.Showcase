using Microsoft.Extensions.Options;
using RustServer.Plugins.Features.PluginManagement.Git;
using RustServer.Plugins.Features.PluginManagement.LocalGit;
using RustServer.Plugins.Features.PluginManagement.Models;
using RustServer.Plugins.Features.PluginManagement.Models.Git;

namespace RustServer.Plugins.Features.PluginManagement.RemoteGit;

public sealed class RemoteGit(IOptions<LocalGitOptions> options, GitProcess gitProcess) : IRemoteGit
{
    public const string CanonicalRemoteName = "origin";

    private readonly LocalGitOptions localGitOptions = options.Value;

    public async Task<RemoteGitFetchResult> FetchAsync(
        RepositoryId repository,
        CancellationToken cancellationToken)
    {
        var workspacePath = localGitOptions.GetWorkspacePath(repository);
        var validationStatus = await ValidateWorkspaceAsync(workspacePath, cancellationToken);
        if (validationStatus is not RemoteGitFetchStatus.Succeeded)
        {
            return new RemoteGitFetchResult(repository, validationStatus);
        }

        var fetchResult = await ExecuteRemoteAsync(
            repository,
            workspacePath,
            ["fetch", "--prune", CanonicalRemoteName],
            cancellationToken);

        return new RemoteGitFetchResult(
            repository,
            fetchResult.ExitCode == 0
                ? RemoteGitFetchStatus.Succeeded
                : RemoteGitFetchStatus.Failed);
    }

    public async Task<RemoteGitRepositoryState> GetStateAsync(
        RepositoryId repository,
        string? trackingBranch,
        CancellationToken cancellationToken)
    {
        var workspacePath = localGitOptions.GetWorkspacePath(repository);
        if (!Directory.Exists(workspacePath))
        {
            return new RemoteGitRepositoryState(
                repository,
                false,
                null,
                null,
                RemoteGitDiagnostic.WorkspaceMissing);
        }

        var workTreeResult = await ExecuteReadOnlyAsync(
            workspacePath,
            ["rev-parse", "--is-inside-work-tree"],
            cancellationToken);
        if (workTreeResult.ExitCode != 0 ||
            !string.Equals(workTreeResult.StandardOutput.Trim(), "true", StringComparison.Ordinal))
        {
            return new RemoteGitRepositoryState(
                repository,
                false,
                null,
                null,
                RemoteGitDiagnostic.NotRepository);
        }

        if (!await HasCanonicalRemoteAsync(workspacePath, cancellationToken))
        {
            return new RemoteGitRepositoryState(
                repository,
                false,
                null,
                null,
                RemoteGitDiagnostic.RemoteMissing);
        }

        if (string.IsNullOrEmpty(trackingBranch))
        {
            return new RemoteGitRepositoryState(
                repository,
                true,
                null,
                null,
                RemoteGitDiagnostic.None);
        }

        if (!trackingBranch.StartsWith($"{CanonicalRemoteName}/", StringComparison.Ordinal))
        {
            return new RemoteGitRepositoryState(
                repository,
                true,
                trackingBranch,
                null,
                RemoteGitDiagnostic.TrackingRemoteUnsupported);
        }

        var headResult = await ExecuteReadOnlyAsync(
            workspacePath,
            ["rev-parse", "--verify", trackingBranch],
            cancellationToken);
        if (headResult.ExitCode != 0)
        {
            return new RemoteGitRepositoryState(
                repository,
                true,
                trackingBranch,
                null,
                RemoteGitDiagnostic.TrackingReferenceMissing);
        }

        return new RemoteGitRepositoryState(
            repository,
            true,
            trackingBranch,
            new GitCommitSha(headResult.StandardOutput.Trim()),
            RemoteGitDiagnostic.None);
    }

    public async Task<RemoteGitReleaseBranchesResult> GetReleaseBranchesAsync(
        RepositoryId repository,
        CancellationToken cancellationToken)
    {
        var workspacePath = localGitOptions.GetWorkspacePath(repository);
        var repositoryState = await GetStateAsync(repository, trackingBranch: null, cancellationToken);
        if (repositoryState.Diagnostic != RemoteGitDiagnostic.None)
        {
            return new RemoteGitReleaseBranchesResult(
                repository,
                [],
                repositoryState.Diagnostic);
        }

        var branchesResult = await ExecuteReadOnlyAsync(
            workspacePath,
            [
                "for-each-ref",
                "--format=%(refname:strip=3)%09%(objectname)",
                "refs/remotes/origin/release/"
            ],
            cancellationToken);

        if (branchesResult.ExitCode != 0)
        {
            throw new RemoteGitException("Could not read remote-tracking release branches.");
        }

        return new RemoteGitReleaseBranchesResult(
            repository,
            ParseReleaseBranches(branchesResult.StandardOutput),
            RemoteGitDiagnostic.None);
    }

    public async Task<RemoteGitPushResult> PushAsync(
        RepositoryId repository,
        Source source,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);

        var workspacePath = localGitOptions.GetWorkspacePath(repository);
        var validationStatus = await ValidateWorkspaceAsync(workspacePath, cancellationToken);
        if (validationStatus is not RemoteGitFetchStatus.Succeeded)
        {
            return new RemoteGitPushResult(repository, ToPushStatus(validationStatus));
        }

        var pushResult = await ExecuteRemoteAsync(
            repository,
            workspacePath,
            ["push", CanonicalRemoteName, source.BranchName],
            cancellationToken);
        return new RemoteGitPushResult(
            repository,
            pushResult.ExitCode == 0 ? RemoteGitPushStatus.Succeeded : RemoteGitPushStatus.Failed);
    }

    private async Task<RemoteGitFetchStatus> ValidateWorkspaceAsync(
        string workspacePath,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(workspacePath))
        {
            return RemoteGitFetchStatus.WorkspaceMissing;
        }

        var workTreeResult = await ExecuteReadOnlyAsync(
            workspacePath,
            ["rev-parse", "--is-inside-work-tree"],
            cancellationToken);
        if (workTreeResult.ExitCode != 0 ||
            !string.Equals(workTreeResult.StandardOutput.Trim(), "true", StringComparison.Ordinal))
        {
            return RemoteGitFetchStatus.NotRepository;
        }

        return await HasCanonicalRemoteAsync(workspacePath, cancellationToken)
            ? RemoteGitFetchStatus.Succeeded
            : RemoteGitFetchStatus.RemoteMissing;
    }

    private async Task<bool> HasCanonicalRemoteAsync(
        string workspacePath,
        CancellationToken cancellationToken)
    {
        var result = await ExecuteReadOnlyAsync(
            workspacePath,
            ["remote", "get-url", CanonicalRemoteName],
            cancellationToken);

        return result.ExitCode == 0;
    }

    private static IReadOnlyList<RemoteGitReleaseBranch> ParseReleaseBranches(string output)
    {
        var branches = new List<RemoteGitReleaseBranch>();

        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separatorIndex = line.IndexOf('\t');
            if (separatorIndex <= 0 || separatorIndex == line.Length - 1)
            {
                throw new RemoteGitException("Remote-tracking release branch output is invalid.");
            }

            var branch = line[..separatorIndex];
            var head = line[(separatorIndex + 1)..];
            if (!branch.StartsWith("release/", StringComparison.Ordinal))
            {
                throw new RemoteGitException("Remote-tracking release branch output is invalid.");
            }

            branches.Add(new RemoteGitReleaseBranch(branch, new GitCommitSha(head)));
        }

        return branches;
    }

    private static RemoteGitPushStatus ToPushStatus(RemoteGitFetchStatus status) =>
        status switch
        {
            RemoteGitFetchStatus.Succeeded => RemoteGitPushStatus.Succeeded,
            RemoteGitFetchStatus.WorkspaceMissing => RemoteGitPushStatus.WorkspaceMissing,
            RemoteGitFetchStatus.NotRepository => RemoteGitPushStatus.NotRepository,
            RemoteGitFetchStatus.RemoteMissing => RemoteGitPushStatus.RemoteMissing,
            RemoteGitFetchStatus.Failed => RemoteGitPushStatus.Failed,
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unsupported remote Git status.")
        };

    private async Task<GitCommandResult> ExecuteRemoteAsync(
        RepositoryId repository,
        string workspacePath,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var isPush = arguments.Count > 0 &&
            string.Equals(arguments[0], "push", StringComparison.Ordinal);
        var originCommand = isPush
            ? new[] { "remote", "get-url", "--push", "--all", CanonicalRemoteName }
            : new[] { "remote", "get-url", "--all", CanonicalRemoteName };
        var originResult = await gitProcess.ExecuteIsolatedRemoteReadOnlyAsync(
            workspacePath, originCommand, cancellationToken);
        if (originResult.ExitCode != 0)
        {
            throw new RemoteGitException("Could not read canonical Git origin.");
        }

        var origins = originResult.StandardOutput
            .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        if (origins.Length == 0)
        {
            throw new RemoteGitException("Canonical Git origin has no URL.");
        }

        if (!origins.Any(url => url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
        {
            // Existing SSH/local remotes remain supported until migration.
            return await ExecuteAsync(workspacePath, arguments, cancellationToken);
        }

        var expected = repository switch
        {
            RepositoryId.Plugins => "https://github.com/sysdba925/RustServer.PluginFiles.git",
            RepositoryId.Configs => "https://github.com/sysdba925/RustServer.ConfigFiles.git",
            _ => throw new ArgumentOutOfRangeException(nameof(repository), repository, "Unsupported repository.")
        };

        // Validate every effective fetch/push destination, including Git pushurl and URL rewrites.
        // Multiple destinations and unexpected hosts must never receive the Plugins credential.
        if (origins.Length != 1 ||
            !string.Equals(origins[0], expected, StringComparison.OrdinalIgnoreCase))
        {
            throw new RemoteGitException("HTTPS origin is not the configured Plugins GitHub repository.");
        }

        try
        {
            return await gitProcess.ExecuteAuthenticatedGitHubAsync(workspacePath, arguments, cancellationToken);
        }
        catch (GitHubCredentialUnavailableException exception)
        {
            throw new RemoteGitException("Plugins GitHub credential is unavailable.", exception);
        }
        catch (GitProcessException exception)
        {
            throw new RemoteGitException("Authenticated Git operation is unavailable.", exception);
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
            throw new RemoteGitException("Git is unavailable while reading remote-tracking state.", exception);
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
            throw new RemoteGitException("Git is unavailable while fetching the remote.", exception);
        }
    }
}
