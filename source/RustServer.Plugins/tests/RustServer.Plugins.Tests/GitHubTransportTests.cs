using System.Diagnostics;
using Microsoft.Extensions.Options;
using RustServer.Plugins.Features.PluginManagement.Git;
using RustServer.Plugins.Features.PluginManagement.LocalGit;
using RustServer.Plugins.Features.PluginManagement.Models;
using RustServer.Plugins.Features.PluginManagement.Models.Git;
using RustServer.Plugins.Features.PluginManagement.RemoteGit;
using Xunit;

namespace RustServer.Plugins.Tests;

public sealed class GitHubTransportTests
{
    [Theory]
    [InlineData("#!/bin/sh\r\nexit 0\r\n")]
    [InlineData("#!/bin/sh\nexit 0\n")]
    [InlineData("#!/bin/sh\rexit 0\r")]
    public void GitAskPass_NormalizesHostLineEndingsToUnixLf(string source)
    {
        Assert.Equal("#!/bin/sh\nexit 0\n", GitHubAskPassSession.NormalizeScriptLineEndings(source));
    }

    [Fact]
    public void GitAskPass_WrittenScriptHasUnixShebangAndNoCarriageReturns()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        using var askPass = new GitHubAskPassSession();
        var startInfo = new ProcessStartInfo();
        askPass.Configure(startInfo);
        var scriptPath = startInfo.Environment["GIT_ASKPASS"];
        Assert.False(string.IsNullOrWhiteSpace(scriptPath));

        var content = File.ReadAllText(scriptPath!);
        Assert.StartsWith("#!/bin/sh\n", content, StringComparison.Ordinal);
        Assert.DoesNotContain("\r", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GitProcess_ReadOnlyStdinSupportsNullDelimitedInput()
    {
        using var repository = await GitTestRepository.CreateAsync();
        await repository.AddWorkspaceCommitAsync(".gitignore", "data/*\n"u8.ToArray());
        var git = new GitProcess();

        var result = await git.ExecuteReadOnlyWithStandardInputAsync(
            repository.WorkspacePath,
            ["check-ignore", "--stdin", "-z"],
            System.Text.Encoding.UTF8.GetBytes("data/ignored.json\0config/allowed.json\0"),
            CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("data/ignored.json\0", result.StandardOutput);
    }

    [Fact]
    public async Task GitProcess_FailedCommand_PreservesStderrInResult()
    {
        var git = new GitProcess();
        var result = await git.ExecuteAsync(
            Path.GetTempPath(),
            ["--rustserver-invalid-git-option"],
            CancellationToken.None);

        Assert.NotEqual(0, result.ExitCode);
        Assert.False(string.IsNullOrWhiteSpace(result.StandardError));
    }

    [Fact]
    public async Task GitAskPass_ReadsCredentialFromFile_WithoutAnInteractivePrompt()
    {
        if (!OperatingSystem.IsLinux())
        {
            return; // The production credential transport runs on Linux.
        }

        var directory = Path.Combine(Path.GetTempPath(), "RustServer.Plugins.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var credential = Path.Combine(directory, "credential");
        const string dummyToken = "test-only-token-not-secret";
        try
        {
            await File.WriteAllTextAsync(credential, dummyToken + "\n");
            using var askPass = new GitHubAskPassSession();
            var startInfo = new ProcessStartInfo
            {
                FileName = "git",
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            startInfo.ArgumentList.Add("-c");
            startInfo.ArgumentList.Add("credential.helper=");
            startInfo.ArgumentList.Add("credential");
            startInfo.ArgumentList.Add("fill");
            startInfo.Environment[SystemdGitHubTokenProvider.CredentialPathEnvironmentVariable] = credential;
            askPass.Configure(startInfo);

            Assert.DoesNotContain(dummyToken, startInfo.ArgumentList);
            Assert.DoesNotContain(dummyToken, startInfo.Environment.Values);

            using var process = Process.Start(startInfo) ??
                throw new InvalidOperationException("Git process did not start.");
            await process.StandardInput.WriteAsync("protocol=https\nhost=github.com\n\n");
            process.StandardInput.Close();
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            await errorTask;

            Assert.Equal(0, process.ExitCode);
            Assert.Contains("username=x-access-token", await outputTask, StringComparison.Ordinal);
            Assert.Contains("password=" + dummyToken, await outputTask, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Fetch_RejectsUnexpectedHttpsOrigin_BeforeCredentialIsRead()
    {
        using var repository = await GitTestRepository.CreateAsync();
        await repository.ExecuteWorkspaceGitAsync(
            "remote", "set-url", "origin", "https://example.invalid/other.git");
        var provider = new SystemdGitHubTokenProvider(() =>
            throw new InvalidOperationException("Credential should not be accessed."));
        var git = new RemoteGit(
            Options.Create(new LocalGitOptions
            {
                Plugins = new WorkspaceOptions { WorkspacePath = repository.WorkspacePath },
                Configs = new WorkspaceOptions { WorkspacePath = repository.WorkspacePath }
            }),
            new GitProcess(provider));

        var exception = await Assert.ThrowsAsync<RemoteGitException>(() =>
            git.FetchAsync(RepositoryId.Plugins, CancellationToken.None));

        Assert.Contains("HTTPS origin", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Push_RejectsUntrustedPushUrl_EvenWhenFetchOriginIsCanonical()
    {
        using var repository = await GitTestRepository.CreateAsync();
        await repository.ExecuteWorkspaceGitAsync(
            "remote", "set-url", "origin", "https://github.com/sysdba925/RustServer.PluginFiles.git");
        await repository.ExecuteWorkspaceGitAsync(
            "remote", "set-url", "--push", "origin", "https://example.invalid/receive.git");

        var provider = new SystemdGitHubTokenProvider(() =>
            throw new InvalidOperationException("Credential should not be accessed."));
        var git = new RemoteGit(
            Options.Create(new LocalGitOptions
            {
                Plugins = new WorkspaceOptions { WorkspacePath = repository.WorkspacePath },
                Configs = new WorkspaceOptions { WorkspacePath = repository.WorkspacePath }
            }),
            new GitProcess(provider));

        var exception = await Assert.ThrowsAsync<RemoteGitException>(() =>
            git.PushAsync(RepositoryId.Plugins, Source.Development, CancellationToken.None));

        Assert.Contains("HTTPS origin", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExistingFilesystemOrigin_StillFetchesWithoutGithubCredential()
    {
        using var repository = await GitTestRepository.CreateAsync();
        var provider = new SystemdGitHubTokenProvider(() =>
            throw new InvalidOperationException("Credential should not be accessed."));
        var git = new RemoteGit(
            Options.Create(new LocalGitOptions
            {
                Plugins = new WorkspaceOptions { WorkspacePath = repository.WorkspacePath },
                Configs = new WorkspaceOptions { WorkspacePath = repository.WorkspacePath }
            }),
            new GitProcess(provider));

        var result = await git.FetchAsync(RepositoryId.Plugins, CancellationToken.None);

        Assert.Equal(RemoteGitFetchStatus.Succeeded, result.Status);
    }
}
