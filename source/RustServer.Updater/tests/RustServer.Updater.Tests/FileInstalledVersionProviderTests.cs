using System.Diagnostics;
using RustServer.Updater.Api.Applications;

namespace RustServer.Updater.Tests;

public sealed class FileInstalledVersionProviderTests : IDisposable
{
    private readonly string _installationPath = Path.Combine(
        Path.GetTempPath(),
        $"RustServer.Updater.Tests.{Guid.NewGuid():N}");

    [Fact]
    public void GetInstalledVersion_ReturnsTargetDirectoryNameForExistingCurrentLink()
    {
        var releaseDirectory = Directory.CreateDirectory(Path.Combine(_installationPath, "releases", "v1.2.3"));
        CreateDirectoryLink(
            Path.Combine(_installationPath, "current"),
            Path.GetRelativePath(_installationPath, releaseDirectory.FullName));

        var installedVersion = CreateProvider().GetInstalledVersion(CreateApplication());

        Assert.Equal("v1.2.3", installedVersion);
    }

    [Fact]
    public void GetInstalledVersion_ReturnsNullWhenCurrentLinkIsMissing()
    {
        Directory.CreateDirectory(_installationPath);

        var installedVersion = CreateProvider().GetInstalledVersion(CreateApplication());

        Assert.Null(installedVersion);
    }

    [Fact]
    public void GetInstalledVersion_ReturnsNullWhenCurrentLinkIsInvalid()
    {
        Directory.CreateDirectory(_installationPath);
        var currentLink = Path.Combine(_installationPath, "current");
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(currentLink);
        }
        else
        {
            Directory.CreateSymbolicLink(currentLink, Path.Combine("releases", "missing"));
        }

        var installedVersion = CreateProvider().GetInstalledVersion(CreateApplication());

        Assert.Null(installedVersion);
    }

    public void Dispose()
    {
        var currentLink = Path.Combine(_installationPath, "current");
        if (OperatingSystem.IsWindows() && Directory.Exists(currentLink))
        {
            Directory.Delete(currentLink);
        }

        if (Directory.Exists(_installationPath))
        {
            Directory.Delete(_installationPath, recursive: true);
        }
    }

    private FileInstalledVersionProvider CreateProvider() => new();

    private ApplicationRegistration CreateApplication() => new(
        "RustServer.Updater",
        "sysdba925/RustServer.Updater",
        _installationPath,
        "rustserver-updater.service",
        "RustServer.Updater.Api.dll");

    private static void CreateDirectoryLink(string linkPath, string targetPath)
    {
        if (!OperatingSystem.IsWindows())
        {
            Directory.CreateSymbolicLink(linkPath, targetPath);
            return;
        }

        var targetFullPath = Path.GetFullPath(targetPath, Path.GetDirectoryName(linkPath)!);

        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c mklink /J \"{linkPath}\" \"{targetFullPath}\"",
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true
        });

        process!.WaitForExit();
        Assert.True(process.ExitCode == 0, process.StandardError.ReadToEnd());
    }
}
