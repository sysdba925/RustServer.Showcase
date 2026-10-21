using Microsoft.Extensions.Options;
using RustServer.Plugins.Features.PluginManagement.Git;
using RustServer.Plugins.Features.PluginManagement.LocalGit;
using RustServer.Plugins.Features.PluginManagement.Models.Git;

namespace RustServer.Plugins.Features.PluginManagement.RemoteGit;

/// <summary>Creates missing Plugins and Configs working copies; never resets an existing checkout.</summary>
public sealed class GitWorkspaceInitializer(IOptions<LocalGitOptions> options, GitProcess git)
{
    private static readonly (RepositoryId Repository, string Url, string SshUrl)[] Sources =
    [
        (RepositoryId.Configs, "https://github.com/sysdba925/RustServer.ConfigFiles.git", "git@github.com:sysdba925/RustServer.ConfigFiles.git"),
        (RepositoryId.Plugins, "https://github.com/sysdba925/RustServer.PluginFiles.git", "git@github.com:sysdba925/RustServer.PluginFiles.git")
    ];

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        foreach (var source in Sources)
        {
            try
            {
                await InitializeOneAsync(
                    options.Value.GetWorkspacePath(source.Repository),
                    source.Url,
                    source.SshUrl,
                    cancellationToken);
            }
            catch (GitWorkspaceInitializationException exception)
            {
                // The repository identifier is fixed and safe for production logs.
                throw new GitWorkspaceInitializationException(
                    $"Git workspace initialization failed for {source.Repository}: {exception.Message}");
            }
        }
    }

    // Internal seam for offline Git-repository tests, without altering production remote URLs.
    internal async Task InitializeOneAsync(
        string workspacePath,
        string originUrl,
        string? allowedLegacySshUrl,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspacePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(originUrl);

        string target;
        try
        {
            target = Path.GetFullPath(workspacePath);
            if (target == Path.GetPathRoot(target))
            {
                throw new GitWorkspaceInitializationException("The Git workspace target is unsafe.");
            }
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException)
        {
            throw new GitWorkspaceInitializationException("The Git workspace path is invalid.");
        }

        var parent = Path.GetDirectoryName(target) ??
            throw new GitWorkspaceInitializationException("The Git workspace parent is invalid.");
        if (Directory.Exists(parent) && IsLink(parent))
        {
            throw new GitWorkspaceInitializationException("The Git workspace parent cannot be a symbolic link.");
        }

        if (File.Exists(target) || (Directory.Exists(target) && IsLink(target)))
        {
            throw new GitWorkspaceInitializationException("The Git workspace target is not a safe directory.");
        }

        if (Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target).Any())
        {
            await ValidateExistingAsync(target, originUrl, allowedLegacySshUrl, cancellationToken);
            return;
        }

        // Bootstrap may create an empty target; the service owns its first clone.
        Directory.CreateDirectory(parent);
        var staging = Path.Combine(parent, ".rustserver-clone-" + Guid.NewGuid().ToString("N"));
        try
        {
            var args = new[] { "clone", "--branch", "develop", "--origin", "origin", originUrl, staging };
            var result = originUrl.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase)
                ? await git.ExecuteAuthenticatedGitHubAsync(parent, args, cancellationToken)
                : await git.ExecuteAsync(parent, args, cancellationToken);
            if (result.ExitCode != 0)
            {
                throw new GitWorkspaceInitializationException(
                    $"Could not clone the Git source repository (exitCode={result.ExitCode}, " +
                    $"diagnostic={GitCloneFailureDiagnostic.Classify(result.StandardError)}).");
            }

            // Never delete a populated target, including one created concurrently by another process.
            if (Directory.Exists(target))
            {
                if (Directory.EnumerateFileSystemEntries(target).Any() || IsLink(target))
                {
                    throw new GitWorkspaceInitializationException("The Git workspace changed during initialization.");
                }
                Directory.Delete(target);
            }
            Directory.Move(staging, target);
        }
        catch (GitHubCredentialUnavailableException)
        {
            throw new GitWorkspaceInitializationException("The Plugins GitHub credential is unavailable.");
        }
        catch (GitProcessException)
        {
            throw new GitWorkspaceInitializationException("Git is unavailable during workspace initialization.");
        }
        finally
        {
            // The only recursive deletion is a newly generated temporary staging directory.
            if (Directory.Exists(staging))
            {
                Directory.Delete(staging, recursive: true);
            }
        }
    }

    private async Task ValidateExistingAsync(
        string target,
        string expectedUrl,
        string? legacyUrl,
        CancellationToken cancellationToken)
    {
        var gitMetadata = Path.Combine(target, ".git");
        if ((!Directory.Exists(gitMetadata) && !File.Exists(gitMetadata)) ||
            IsLink(gitMetadata))
        {
            throw new GitWorkspaceInitializationException("Existing workspace Git metadata is missing or unsafe.");
        }

        var root = await git.ExecuteReadOnlyAsync(
            target, ["rev-parse", "--show-toplevel"], cancellationToken);
        if (root.ExitCode != 0 ||
            !string.Equals(Path.GetFullPath(root.StandardOutput.Trim()), target, StringComparison.Ordinal))
        {
            throw new GitWorkspaceInitializationException("Existing workspace Git root is invalid.");
        }

        var remote = await git.ExecuteReadOnlyAsync(
            target, ["remote", "get-url", "--all", "origin"], cancellationToken);
        var urls = remote.StandardOutput.Split(
            '\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (remote.ExitCode != 0 || urls.Length != 1 ||
            !(string.Equals(urls[0], expectedUrl, StringComparison.OrdinalIgnoreCase) ||
              (legacyUrl is not null && string.Equals(urls[0], legacyUrl, StringComparison.Ordinal))))
        {
            throw new GitWorkspaceInitializationException("Existing workspace origin does not match the managed repository.");
        }

        // Leave the checkout, local modifications, HEAD and tracking refs exactly as found.
    }

    private static bool IsLink(string path) =>
        File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint);
}

public sealed class GitWorkspaceInitializationException(string message) : Exception(message);
