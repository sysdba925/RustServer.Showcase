using System.ComponentModel;
using System.Diagnostics;

namespace RustServer.Plugins.Features.PluginManagement.Git;

public sealed class GitProcess
{
    private readonly IGitHubTokenProvider? tokenProvider;

    // Preserve existing local-only callers; dependency injection selects the next constructor.
    public GitProcess() { }

    public GitProcess(IGitHubTokenProvider tokenProvider)
    {
        this.tokenProvider = tokenProvider ?? throw new ArgumentNullException(nameof(tokenProvider));
    }

    internal async Task<GitCommandResult> ExecuteAuthenticatedGitHubAsync(
        string workspacePath,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        if (tokenProvider is null)
        {
            throw new GitProcessException("Plugins GitHub credential provider is not configured.");
        }

        // Validate the credential without passing the token via argv or environment.
        _ = tokenProvider.GetToken();
        using var askPass = new GitHubAskPassSession();

        return await ExecuteAsync(
            workspacePath,
            arguments,
            disableOptionalLocks: false,
            cancellationToken,
            startInfo =>
            {
                startInfo.ArgumentList.Add("-c");
                startInfo.ArgumentList.Add("credential.helper=");
                startInfo.ArgumentList.Add("-c");
                startInfo.ArgumentList.Add("http.followRedirects=false");
                askPass.Configure(startInfo);
            });
    }

    // Resolve effective Git URLs under the exact same config isolation used
    // for token-bearing fetch/push/clone. Otherwise URL rewrite preflight may
    // inspect a different origin than the authenticated command.
    internal Task<GitCommandResult> ExecuteIsolatedRemoteReadOnlyAsync(
        string workspacePath,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            workspacePath,
            arguments,
            disableOptionalLocks: true,
            cancellationToken,
            startInfo =>
            {
                startInfo.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
                startInfo.Environment["GIT_CONFIG_GLOBAL"] = "/dev/null";
            });

    /// <summary>Executes a read-only Git command with binary-safe (NUL-delimited) stdin.</summary>
    public Task<GitCommandResult> ExecuteReadOnlyWithStandardInputAsync(
        string workspacePath,
        IReadOnlyList<string> arguments,
        ReadOnlyMemory<byte> input,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfZero(input.Length);
        return ExecuteAsync(
            workspacePath, arguments, disableOptionalLocks: true, cancellationToken,
            standardInput: input);
    }

    public Task<GitCommandResult> ExecuteReadOnlyAsync(
        string workspacePath,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken) =>
        ExecuteAsync(workspacePath, arguments, disableOptionalLocks: true, cancellationToken);

    public Task<GitCommandResult> ExecuteAsync(
        string workspacePath,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken) =>
        ExecuteAsync(workspacePath, arguments, disableOptionalLocks: false, cancellationToken);

    public async Task<GitBinaryCommandResult> ExecuteReadOnlyForBytesAsync(
        string workspacePath,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var startInfo = CreateStartInfo(workspacePath, arguments, disableOptionalLocks: true);

        try
        {
            using var process = Process.Start(startInfo) ??
                throw new GitProcessException("Could not start the Git process.");
            await using var output = new MemoryStream();
            var standardOutputTask = process.StandardOutput.BaseStream.CopyToAsync(output, CancellationToken.None);
            var standardErrorTask = process.StandardError.ReadToEndAsync(CancellationToken.None);

            await WaitForExitOrCancelAsync(process, standardOutputTask, standardErrorTask, cancellationToken);

            return new GitBinaryCommandResult(process.ExitCode, output.ToArray());
        }
        catch (Win32Exception exception)
        {
            throw new GitProcessException("Could not start the Git process.", exception);
        }
    }

    private static async Task<GitCommandResult> ExecuteAsync(
        string workspacePath,
        IReadOnlyList<string> arguments,
        bool disableOptionalLocks,
        CancellationToken cancellationToken,
        Action<ProcessStartInfo>? configureStartInfo = null,
        ReadOnlyMemory<byte> standardInput = default)
    {
        var startInfo = CreateStartInfo(workspacePath, arguments, disableOptionalLocks, configureStartInfo);
        if (!standardInput.IsEmpty)
        {
            startInfo.RedirectStandardInput = true;
        }

        try
        {
            using var process = Process.Start(startInfo) ??
                throw new GitProcessException("Could not start the Git process.");
            var standardOutputTask = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
            var standardErrorTask = process.StandardError.ReadToEndAsync(CancellationToken.None);

            if (!standardInput.IsEmpty)
            {
                try
                {
                    await process.StandardInput.BaseStream.WriteAsync(standardInput, cancellationToken);
                    process.StandardInput.Close(); // EOF is required by git check-ignore --stdin.
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    if (!process.HasExited)
                    {
                        try { process.Kill(entireProcessTree: true); }
                        catch (InvalidOperationException) { }
                        catch (Win32Exception) { }
                    }

                    await process.WaitForExitAsync(CancellationToken.None);
                    throw;
                }
            }

            await WaitForExitOrCancelAsync(process, standardOutputTask, standardErrorTask, cancellationToken);

            return new GitCommandResult(process.ExitCode, await standardOutputTask)
            {
                StandardError = await standardErrorTask
            };
        }
        catch (Win32Exception exception)
        {
            throw new GitProcessException("Could not start the Git process.", exception);
        }
    }

    private static async Task WaitForExitOrCancelAsync(
        Process process,
        Task standardOutputTask,
        Task standardErrorTask,
        CancellationToken cancellationToken)
    {
        try
        {
            await process.WaitForExitAsync(cancellationToken);
            await Task.WhenAll(standardOutputTask, standardErrorTask);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (!process.HasExited)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                }
                catch (Win32Exception)
                {
                }
            }

            await process.WaitForExitAsync(CancellationToken.None);
            try
            {
                await Task.WhenAll(standardOutputTask, standardErrorTask);
            }
            catch
            {
                // Cancellation remains the operation result after the child process has stopped.
            }

            throw;
        }
    }

    private static ProcessStartInfo CreateStartInfo(
        string workspacePath,
        IReadOnlyList<string> arguments,
        bool disableOptionalLocks,
        Action<ProcessStartInfo>? configureStartInfo = null)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workspacePath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        if (disableOptionalLocks)
        {
            startInfo.Environment["GIT_OPTIONAL_LOCKS"] = "0";
        }

        configureStartInfo?.Invoke(startInfo);

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }
}
