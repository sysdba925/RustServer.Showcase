using System.Diagnostics;
using Microsoft.Extensions.Options;
using RustServer.Plugins.Features.PluginManagement.Models.Git;
using RustServer.Plugins.Features.PluginManagement.Runtime;
using Xunit;

namespace RustServer.Plugins.Tests;

public sealed class RuntimeFilesTests
{
    [Fact]
    public async Task ListsOnlyExplicitlyMappedFilesAndPreservesBinaryContent()
    {
        using var runtimeRoot = new TemporaryRuntimeRoot();
        var oxideRoot = Path.Combine(runtimeRoot.ServerRootPath, "oxide");
        Directory.CreateDirectory(Path.Combine(oxideRoot, "plugins"));
        Directory.CreateDirectory(Path.Combine(oxideRoot, "config"));
        Directory.CreateDirectory(Path.Combine(oxideRoot, "data", "Example"));
        Directory.CreateDirectory(Path.Combine(oxideRoot, "configToolKit"));
        await File.WriteAllTextAsync(Path.Combine(oxideRoot, "plugins", "Plugin.cs"), "// plugin");
        await File.WriteAllBytesAsync(Path.Combine(oxideRoot, "plugins", "Plugin.zip"), [0x50, 0x4B]);
        await File.WriteAllTextAsync(Path.Combine(oxideRoot, "config", "Test.json"), "{}");
        var binaryContent = new byte[] { 0x00, 0xFF, 0x89, 0x50, 0x4E, 0x47 };
        await File.WriteAllBytesAsync(Path.Combine(oxideRoot, "data", "Example", "image.png"), binaryContent);
        await File.WriteAllTextAsync(Path.Combine(oxideRoot, "configToolKit", "ignored.json"), "{}");

        var mapping = new ManagedFileMapping();
        var runtimeFiles = CreateRuntimeFiles(runtimeRoot.ServerRootPath, mapping);

        var files = await runtimeFiles.ListAsync(CancellationToken.None);

        Assert.Collection(
            files,
            file => AssertManagedFile(file, RepositoryId.Configs, "config/Test.json", "oxide/config/Test.json"),
            file => AssertManagedFile(file, RepositoryId.Configs, "data/Example/image.png", "oxide/data/Example/image.png"),
            file => AssertManagedFile(file, RepositoryId.Plugins, "Plugin.cs", "oxide/plugins/Plugin.cs"));
        Assert.Null(mapping.FromRepositoryPath(RepositoryId.Plugins, "Plugin.zip"));
        Assert.Null(mapping.FromRepositoryPath(RepositoryId.Configs, "configToolKit/ignored.json"));

        var binaryFile = Assert.Single(files, file => file.RuntimeRelativePath == "oxide/data/Example/image.png");
        Assert.Equal(binaryContent, await runtimeFiles.ReadAsync(binaryFile, CancellationToken.None));
    }

    [Fact]
    public async Task RejectsTraversalAndSymlinkedManagedPaths()
    {
        using var runtimeRoot = new TemporaryRuntimeRoot();
        var oxideRoot = Path.Combine(runtimeRoot.ServerRootPath, "oxide");
        var configRoot = Path.Combine(oxideRoot, "config");
        var outsideRoot = Path.Combine(runtimeRoot.RootPath, "outside");
        Directory.CreateDirectory(configRoot);
        Directory.CreateDirectory(outsideRoot);
        await File.WriteAllTextAsync(Path.Combine(outsideRoot, "escape.json"), "outside");

        var mapping = new ManagedFileMapping();
        var runtimeFiles = CreateRuntimeFiles(runtimeRoot.ServerRootPath, mapping);
        Assert.Null(mapping.FromRepositoryPath(RepositoryId.Configs, "config/../escape.json"));
        Assert.Null(mapping.FromRuntimePath("oxide/config/../../escape.json"));

        var linkedDirectoryPath = Path.Combine(configRoot, "linked");
        CreateDirectoryLink(linkedDirectoryPath, outsideRoot);
        var linkedFile = mapping.FromRepositoryPath(RepositoryId.Configs, "config/linked/escape.json")!;

        await Assert.ThrowsAsync<RuntimeFilesException>(() =>
            runtimeFiles.ReadAsync(linkedFile, CancellationToken.None));
        await Assert.ThrowsAsync<RuntimeFilesException>(() =>
            runtimeFiles.WriteAsync(linkedFile, new byte[] { 0x01 }, CancellationToken.None));
        await Assert.ThrowsAsync<RuntimeFilesException>(() =>
            runtimeFiles.DeleteAsync(linkedFile, CancellationToken.None));
        Assert.Equal("outside", await File.ReadAllTextAsync(Path.Combine(outsideRoot, "escape.json")));
    }

    private static RuntimeFiles CreateRuntimeFiles(string serverRootPath, ManagedFileMapping mapping) =>
        new(Options.Create(new RuntimeOptions { ServerRootPath = serverRootPath }), mapping);

    private static void CreateDirectoryLink(string linkPath, string targetPath)
    {
        try
        {
            Directory.CreateSymbolicLink(linkPath, targetPath);
        }
        catch (Exception exception) when (
            OperatingSystem.IsWindows() &&
            exception is UnauthorizedAccessException or IOException)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("/c");
            startInfo.ArgumentList.Add("mklink");
            startInfo.ArgumentList.Add("/J");
            startInfo.ArgumentList.Add(linkPath);
            startInfo.ArgumentList.Add(targetPath);

            using var process = Process.Start(startInfo) ??
                throw new InvalidOperationException("Could not create a test directory junction.");
            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"Could not create a test directory junction: {process.StandardError.ReadToEnd()}");
            }
        }
    }

    private static void AssertManagedFile(
        ManagedFile file,
        RepositoryId repository,
        string repositoryPath,
        string runtimePath)
    {
        Assert.Equal(repository, file.Repository);
        Assert.Equal(repositoryPath, file.RepositoryRelativePath);
        Assert.Equal(runtimePath, file.RuntimeRelativePath);
    }

    private sealed class TemporaryRuntimeRoot : IDisposable
    {
        public string RootPath { get; } = Path.Combine(
            Path.GetTempPath(),
            "RustServer.Plugins.Tests",
            Guid.NewGuid().ToString("N"));

        public string ServerRootPath => Path.Combine(RootPath, "server");

        public void Dispose()
        {
            if (Directory.Exists(RootPath))
            {
                RemoveReparsePoints(RootPath);
                Directory.Delete(RootPath, recursive: true);
            }
        }

        private static void RemoveReparsePoints(string directoryPath)
        {
            foreach (var entryPath in Directory.EnumerateFileSystemEntries(directoryPath))
            {
                var attributes = File.GetAttributes(entryPath);
                if (attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    if (attributes.HasFlag(FileAttributes.Directory))
                    {
                        Directory.Delete(entryPath);
                    }
                    else
                    {
                        File.Delete(entryPath);
                    }

                    continue;
                }

                if (attributes.HasFlag(FileAttributes.Directory))
                {
                    RemoveReparsePoints(entryPath);
                }
            }
        }
    }
}
