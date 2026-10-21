using RustServer.Plugins.Features.PluginManagement.Models;
using RustServer.Plugins.Features.PluginManagement.Models.Git;
using Xunit;

namespace RustServer.Plugins.Tests;

public sealed class PluginManagementModelsTests
{
    [Fact]
    public void Staging_AllowsDevelopment()
    {
        var isAllowed = EnvironmentSourcePolicy.IsAllowed(
            PluginEnvironment.Staging,
            Source.Development);

        Assert.True(isAllowed);
    }

    [Fact]
    public void Production_RejectsDevelopment()
    {
        var isAllowed = EnvironmentSourcePolicy.IsAllowed(
            PluginEnvironment.Production,
            Source.Development);

        Assert.False(isAllowed);
    }

    [Fact]
    public void ReleaseBaseline_ContainsTheAppliedReleaseAndRepositoryCommits()
    {
        var release = new Release("2.0");
        var baseline = new Baseline(
            PluginEnvironment.Production,
            Source.ForRelease(release),
            new GitCommitSha("a1b2c3d"),
            new GitCommitSha("d4e5f6a"),
            new DateTimeOffset(2026, 10, 8, 12, 0, 0, TimeSpan.Zero));

        Assert.Equal(release, baseline.Release);
        Assert.Equal("release/2.0", baseline.Source.BranchName);
        Assert.Equal("a1b2c3d", baseline.PluginsCommit.Value);
        Assert.Equal("d4e5f6a", baseline.ConfigsCommit.Value);
    }
}
