using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RustServer.Versions.Api.Versions;

namespace RustServer.Versions.Tests;

public sealed class OxideVersionProviderTests
{
    [Theory]
    [InlineData("2.0.7726", "2.0.7726", false)]
    [InlineData("2.0.7723", "2.0.7726", true)]
    public async Task GetVersionInfoAsync_ComparesInstalledAndAvailableVersions(
        string installedVersion,
        string availableVersion,
        bool expectedUpdateAvailable)
    {
        var provider = CreateProvider(installedVersion, "2.0.7723", availableVersion);

        var result = await provider.GetVersionInfoAsync(CancellationToken.None);

        Assert.Equal(installedVersion, result.InstalledVersion);
        Assert.Equal("2.0.7723", result.RunningVersion);
        Assert.Equal(availableVersion, result.AvailableVersion);
        Assert.Equal(expectedUpdateAvailable, result.UpdateAvailable);
    }

    [Fact]
    public async Task GetVersionInfoAsync_ReturnsNullRunningVersionWhenServerIsStopped()
    {
        var provider = CreateProvider(
            "2.0.7726",
            runningVersion: null,
            availableVersion: "2.0.7726");

        var result = await provider.GetVersionInfoAsync(CancellationToken.None);

        Assert.Equal("2.0.7726", result.InstalledVersion);
        Assert.Null(result.RunningVersion);
        Assert.Equal("2.0.7726", result.AvailableVersion);
        Assert.False(result.UpdateAvailable);
    }

    [Fact]
    public async Task GetVersionInfoAsync_ReportsFirstInstallWhenAssemblyIsMissing()
    {
        var provider = CreateProvider(installedVersion: null, runningVersion: null, availableVersion: "2.0.7726");

        var result = await provider.GetVersionInfoAsync(CancellationToken.None);

        Assert.Null(result.InstalledVersion);
        Assert.Equal("2.0.7726", result.AvailableVersion);
        Assert.True(result.UpdateAvailable);
        Assert.Null(result.RunningVersion);
    }

    [Fact]
    public async Task GetVersionInfoAsync_KeepsInstalledAndAvailableWhenRconResponseIsMalformed()
    {
        var provider = new OxideVersionProvider(
            Options.Create(new OxideVersionsOptions { AssemblyPath = "ignored-by-test" }),
            new FakeInstalledVersionReader("2.0.7830"),
            new RconOxideRunningVersionReader(
                new MalformedRconClient(),
                NullLogger<RconOxideRunningVersionReader>.Instance),
            new FakeAvailableVersionReader("2.0.7830"),
            NullLogger<OxideVersionProvider>.Instance);

        var result = await provider.GetVersionInfoAsync(CancellationToken.None);

        Assert.Equal("2.0.7830", result.InstalledVersion);
        Assert.Null(result.RunningVersion);
        Assert.Equal("2.0.7830", result.AvailableVersion);
        Assert.False(result.UpdateAvailable);
    }

    private static OxideVersionProvider CreateProvider(
        string? installedVersion,
        string? runningVersion,
        string availableVersion)
    {
        return new OxideVersionProvider(
            Options.Create(new OxideVersionsOptions { AssemblyPath = "ignored-by-test" }),
            new FakeInstalledVersionReader(installedVersion),
            new FakeRunningVersionReader(runningVersion),
            new FakeAvailableVersionReader(availableVersion),
            NullLogger<OxideVersionProvider>.Instance);
    }

    private sealed class MalformedRconClient : IRconClient
    {
        public Task<RconCommandResult> ExecuteCommandAsync(
            string command, CancellationToken cancellationToken = default) =>
            Task.FromResult(new RconCommandResult(
                "[Oxide] 20:11 [Info] No Oxide version information available"));
    }

    private sealed class FakeInstalledVersionReader(string? version) : IOxideInstalledVersionReader
    {
        public OxideVersion? ReadVersion(string assemblyPath) =>
            version is null ? null : OxideVersion.Parse(version, "test");
    }

    private sealed class FakeRunningVersionReader(string? version) : IOxideRunningVersionReader
    {
        public Task<OxideVersion?> ReadVersionAsync(CancellationToken cancellationToken) =>
            Task.FromResult(version is null ? null : OxideVersion.Parse(version, "test"));
    }

    private sealed class FakeAvailableVersionReader(string version) : IOxideAvailableVersionReader
    {
        public Task<OxideVersion> ReadVersionAsync(CancellationToken cancellationToken) =>
            Task.FromResult(OxideVersion.Parse(version, "test"));
    }
}
