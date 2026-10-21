using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RustServer.Versions.Api.Versions;

namespace RustServer.Versions.Tests;

public sealed class RustVersionProviderTests
{
    [Theory]
    [InlineData(12345678UL, 12345678UL, false)]
    [InlineData(12345678UL, 87654321UL, true)]
    public async Task GetVersionInfoAsync_ComparesInstalledAndAvailableBuildIds(
        ulong? installedBuildId,
        ulong availableBuildId,
        bool expectedUpdateAvailable)
    {
        var availableReader = new FakeAvailableBuildIdReader(availableBuildId);
        var runningVersion = new RustRunningVersion(
            "2633.288.1",
            new DateTime(2026, 9, 22, 7, 48, 10),
            "6000.3.15x1-11",
            165217,
            "release");
        var provider = CreateProvider(
            installedBuildId,
            availableReader,
            new FakeRunningVersionReader(runningVersion),
            "staging");

        var result = await provider.GetVersionInfoAsync(CancellationToken.None);

        Assert.Equal("staging", result.Branch);
        Assert.Equal(installedBuildId, result.InstalledBuildId);
        Assert.Equal(runningVersion, result.Running);
        Assert.Equal(availableBuildId, result.AvailableBuildId);
        Assert.Equal(expectedUpdateAvailable, result.UpdateAvailable);
        Assert.Equal("staging", availableReader.ReceivedBranch);
    }

    [Fact]
    public async Task GetVersionInfoAsync_ReturnsBuildIdsWhenTheServerIsStopped()
    {
        var availableReader = new FakeAvailableBuildIdReader(12345678UL);
        var provider = CreateProvider(
            12345678UL,
            availableReader,
            new FakeRunningVersionReader(null),
            "public");

        var result = await provider.GetVersionInfoAsync(CancellationToken.None);

        Assert.Equal("public", result.Branch);
        Assert.Equal(12345678UL, result.InstalledBuildId);
        Assert.Null(result.Running);
        Assert.Equal(12345678UL, result.AvailableBuildId);
        Assert.False(result.UpdateAvailable);
    }

    [Fact]
    public async Task GetVersionInfoAsync_ReportsFirstInstallWhenManifestIsMissing()
    {
        var provider = CreateProvider(
            null,
            new FakeAvailableBuildIdReader(12345678UL),
            new FakeRunningVersionReader(null),
            "public");

        var result = await provider.GetVersionInfoAsync(CancellationToken.None);

        Assert.Null(result.InstalledBuildId);
        Assert.Equal(12345678UL, result.AvailableBuildId);
        Assert.True(result.UpdateAvailable);
        Assert.Null(result.Running);
    }

    private static RustVersionProvider CreateProvider(
        ulong? installedBuildId,
        ISteamAvailableBuildIdReader availableReader,
        IRustRunningVersionReader runningVersionReader,
        string branch)
    {
        return new RustVersionProvider(
            Options.Create(new RustVersionsOptions
            {
                AppManifestPath = "ignored-by-test",
                Branch = branch,
            }),
            new FakeInstalledBuildIdReader(installedBuildId),
            availableReader,
            runningVersionReader,
            NullLogger<RustVersionProvider>.Instance);
    }

    private sealed class FakeInstalledBuildIdReader(ulong? buildId) : IInstalledBuildIdReader
    {
        public ulong? ReadBuildId(string appManifestPath) => buildId;
    }

    private sealed class FakeAvailableBuildIdReader(ulong buildId) : ISteamAvailableBuildIdReader
    {
        public string? ReceivedBranch { get; private set; }

        public Task<ulong> ReadBuildIdAsync(string branch, CancellationToken cancellationToken)
        {
            ReceivedBranch = branch;
            return Task.FromResult(buildId);
        }
    }

    private sealed class FakeRunningVersionReader(RustRunningVersion? version) : IRustRunningVersionReader
    {
        public Task<RustRunningVersion?> ReadVersionAsync(CancellationToken cancellationToken) =>
            Task.FromResult(version);
    }
}
