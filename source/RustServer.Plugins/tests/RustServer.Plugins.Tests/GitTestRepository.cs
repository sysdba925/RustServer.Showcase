using System.Diagnostics;

namespace RustServer.Plugins.Tests;

internal sealed class GitTestRepository : IDisposable
{
    private GitTestRepository(string rootPath)
    {
        RootPath = rootPath;
        WorkspacePath = Path.Combine(rootPath, "workspace");
        OriginPath = Path.Combine(rootPath, "origin.git");
    }

    public string RootPath { get; }

    public string WorkspacePath { get; }

    public string OriginPath { get; }

    public static async Task<GitTestRepository> CreateAsync()
    {
        var repository = new GitTestRepository(Path.Combine(
            Path.GetTempPath(),
            "RustServer.Plugins.Tests",
            Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(repository.RootPath);

        try
        {
            await RunGitAsync(repository.RootPath, "init", "--bare", repository.OriginPath);
            await RunGitAsync(repository.RootPath, "init", repository.WorkspacePath);
            await ConfigureIdentityAsync(repository.WorkspacePath);
            await File.WriteAllTextAsync(Path.Combine(repository.WorkspacePath, "README.md"), "initial");
            await RunGitAsync(repository.WorkspacePath, "add", "README.md");
            await RunGitAsync(repository.WorkspacePath, "commit", "-m", "Initial commit");
            await RunGitAsync(repository.WorkspacePath, "branch", "-M", "develop");
            await RunGitAsync(repository.WorkspacePath, "remote", "add", "origin", repository.OriginPath);
            await RunGitAsync(repository.WorkspacePath, "push", "-u", "origin", "develop");
            await RunGitAsync(repository.OriginPath, "symbolic-ref", "HEAD", "refs/heads/develop");

            return repository;
        }
        catch
        {
            repository.Dispose();
            throw;
        }
    }

    public async Task<string> AddRemoteCommitAsync(string fileName)
        => await AddRemoteCommitAsync(fileName, "remote update"u8.ToArray());

    public async Task<string> AddRemoteCommitAsync(string relativePath, byte[] content)
        => await AddRemoteCommitToBranchAsync("develop", relativePath, content);

    public async Task<string> AddRemoteCommitToBranchAsync(
        string branch,
        string relativePath,
        byte[] content)
    {
        var publisherPath = Path.Combine(RootPath, $"publisher-{Guid.NewGuid():N}");

        try
        {
            await RunGitAsync(RootPath, "clone", "--branch", branch, OriginPath, publisherPath);
            await ConfigureIdentityAsync(publisherPath);
            var filePath = Path.Combine(
                publisherPath,
                relativePath.Replace('/', Path.DirectorySeparatorChar));
            var directoryPath = Path.GetDirectoryName(filePath) ??
                throw new InvalidOperationException("Test repository path must have a directory.");
            Directory.CreateDirectory(directoryPath);
            await File.WriteAllBytesAsync(filePath, content);
            await RunGitAsync(publisherPath, "add", "--", relativePath);
            await RunGitAsync(publisherPath, "commit", "-m", $"Add {relativePath}");
            await RunGitAsync(publisherPath, "push", "origin", branch);

            return await GetHeadAsync(publisherPath);
        }
        finally
        {
            DeleteDirectory(publisherPath);
        }
    }

    public async Task<string> AddRemoteReleaseAsync(string version)
    {
        var publisherPath = Path.Combine(RootPath, $"publisher-{Guid.NewGuid():N}");
        var branch = $"release/{version}";

        try
        {
            await RunGitAsync(RootPath, "clone", "--branch", "develop", OriginPath, publisherPath);
            await ConfigureIdentityAsync(publisherPath);
            await RunGitAsync(publisherPath, "checkout", "-b", branch);
            await RunGitAsync(publisherPath, "push", "origin", branch);

            return await GetHeadAsync(publisherPath);
        }
        finally
        {
            DeleteDirectory(publisherPath);
        }
    }

    public async Task<string> AddWorkspaceCommitAsync(string relativePath, byte[] content)
    {
        var filePath = Path.Combine(
            WorkspacePath,
            relativePath.Replace('/', Path.DirectorySeparatorChar));
        var directoryPath = Path.GetDirectoryName(filePath) ??
            throw new InvalidOperationException("Test repository path must have a directory.");
        Directory.CreateDirectory(directoryPath);
        await File.WriteAllBytesAsync(filePath, content);
        await RunGitAsync(WorkspacePath, "add", "--", relativePath);
        await RunGitAsync(WorkspacePath, "commit", "-m", $"Add {relativePath}");

        return await GetHeadAsync();
    }

    public Task RemoveOriginAsync() => RunGitAsync(WorkspacePath, "remote", "remove", "origin");

    public Task<string> GetHeadAsync() => GetHeadAsync(WorkspacePath);

    public async Task<string> GetRemoteHeadAsync(string branch) =>
        (await RunGitAsync(OriginPath, "rev-parse", $"refs/heads/{branch}")).StandardOutput.Trim();

    public async Task<byte[]> GetRemoteFileAsync(string branch, string relativePath)
    {
        var readerPath = Path.Combine(RootPath, $"reader-{Guid.NewGuid():N}");

        try
        {
            await RunGitAsync(RootPath, "clone", "--branch", branch, OriginPath, readerPath);
            return await File.ReadAllBytesAsync(Path.Combine(
                readerPath,
                relativePath.Replace('/', Path.DirectorySeparatorChar)));
        }
        finally
        {
            DeleteDirectory(readerPath);
        }
    }

    public async Task<GitCommitInfo> GetRemoteCommitInfoAsync(string branch)
    {
        var result = await RunGitAsync(
            OriginPath,
            "show",
            "-s",
            "--format=%an%x00%ae%x00%cn%x00%ce%x00%B",
            $"refs/heads/{branch}");
        var parts = result.StandardOutput.Split('\0', 5, StringSplitOptions.None);
        if (parts.Length != 5)
        {
            throw new InvalidOperationException("Git did not return complete commit metadata.");
        }

        return new GitCommitInfo(parts[0], parts[1], parts[2], parts[3], parts[4]);
    }

    public async Task<string> GetCurrentBranchAsync() =>
        (await RunGitAsync(WorkspacePath, "branch", "--show-current")).StandardOutput.Trim();

    public async Task<string> GetTrackingHeadAsync() =>
        (await RunGitAsync(WorkspacePath, "rev-parse", "refs/remotes/origin/develop")).StandardOutput.Trim();

    public async Task FetchOriginAsync() =>
        await RunGitAsync(WorkspacePath, "fetch", "--prune", "origin");

    public async Task ExecuteWorkspaceGitAsync(params string[] arguments) =>
        await RunGitAsync(WorkspacePath, arguments);

    public void Dispose() => DeleteDirectory(RootPath);

    private static async Task ConfigureIdentityAsync(string workspacePath)
    {
        await RunGitAsync(workspacePath, "config", "user.name", "Test User");
        await RunGitAsync(workspacePath, "config", "user.email", "test@example.invalid");
    }

    private static async Task<string> GetHeadAsync(string workspacePath) =>
        (await RunGitAsync(workspacePath, "rev-parse", "HEAD")).StandardOutput.Trim();

    private static async Task<GitTestCommandResult> RunGitAsync(
        string workingDirectory,
        params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo) ??
            throw new InvalidOperationException("Could not start Git for test setup.");
        var standardOutputTask = process.StandardOutput.ReadToEndAsync();
        var standardErrorTask = process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();
        await Task.WhenAll(standardOutputTask, standardErrorTask);

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Git test setup failed: {await standardErrorTask}");
        }

        return new GitTestCommandResult(await standardOutputTask);
    }

    private static void DeleteDirectory(string path)
    {
        const int deletionAttempts = 5;

        for (var attempt = 1; Directory.Exists(path); attempt++)
        {
            try
            {
                ClearReadOnlyFileAttributes(path);
                Directory.Delete(path, recursive: true);
                return;
            }
            catch (UnauthorizedAccessException) when (attempt < deletionAttempts)
            {
                Thread.Sleep(TimeSpan.FromMilliseconds(100));
            }
            catch (IOException) when (attempt < deletionAttempts)
            {
                Thread.Sleep(TimeSpan.FromMilliseconds(100));
            }
        }
    }

    private static void ClearReadOnlyFileAttributes(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        foreach (var filePath in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
        {
            var attributes = File.GetAttributes(filePath);
            if (attributes.HasFlag(FileAttributes.ReadOnly))
            {
                File.SetAttributes(filePath, attributes & ~FileAttributes.ReadOnly);
            }
        }
    }

    private sealed record GitTestCommandResult(string StandardOutput);
}

internal sealed record GitCommitInfo(
    string AuthorName,
    string AuthorEmail,
    string CommitterName,
    string CommitterEmail,
    string Message);
