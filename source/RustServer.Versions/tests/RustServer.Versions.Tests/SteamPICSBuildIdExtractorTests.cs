using RustServer.Versions.Api.Versions;
using SteamKit2;

namespace RustServer.Versions.Tests;

public sealed class SteamPICSBuildIdExtractorTests
{
    [Fact]
    public void Extract_ReadsBuildIdFromConfiguredBranch()
    {
        var metadata = CreateMetadata("public", "87654321");

        var buildId = SteamPICSBuildIdExtractor.Extract(metadata, "public");

        Assert.Equal(87654321UL, buildId);
    }

    [Fact]
    public void Extract_ThrowsWhenConfiguredBranchIsMissing()
    {
        var metadata = CreateMetadata("staging", "87654321");

        var exception = Assert.Throws<InvalidOperationException>(
            () => SteamPICSBuildIdExtractor.Extract(metadata, "public"));

        Assert.Contains("public", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("not-a-build-id")]
    [InlineData("0")]
    public void Extract_ThrowsWhenBuildIdIsInvalid(string buildId)
    {
        var metadata = CreateMetadata("public", buildId);

        Assert.Throws<InvalidOperationException>(() => SteamPICSBuildIdExtractor.Extract(metadata, "public"));
    }

    private static KeyValue CreateMetadata(string branch, string buildId)
    {
        var metadata = new KeyValue();
        var depots = new KeyValue("depots");
        var branches = new KeyValue("branches");
        var branchMetadata = new KeyValue(branch);

        branchMetadata.Children.Add(new KeyValue("buildid", buildId));
        branches.Children.Add(branchMetadata);
        depots.Children.Add(branches);
        metadata.Children.Add(depots);

        return metadata;
    }
}
