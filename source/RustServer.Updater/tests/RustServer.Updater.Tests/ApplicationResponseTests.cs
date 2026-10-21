using RustServer.Updater.Api.Applications;

namespace RustServer.Updater.Tests;

public sealed class ApplicationResponseTests
{
    [Theory]
    [InlineData("v1.0.0-dev.2", "v1.0.0-dev.10", true)]
    [InlineData("v1.0.0-dev.10", "v1.0.0-dev.2", false)]
    [InlineData("v1.0.0-dev.10", "v1.0.0", true)]
    [InlineData("v1.0.0", "v1.0.0-dev.10", false)]
    [InlineData("v1.0.0", "v1.0.1-dev.1", true)]
    public void UpdateAvailable_UsesSemanticVersionPrecedence(
        string installedVersion,
        string availableVersion,
        bool expected)
    {
        var response = new ApplicationResponse(
            "RustServer.Updater",
            installedVersion,
            availableVersion,
            ApplicationStatus.Running);

        Assert.Equal(expected, response.UpdateAvailable);
    }

    [Fact]
    public void UpdateAvailable_IsFalseWhenTheApplicationIsNotInstalled()
    {
        var response = new ApplicationResponse(
            "RustServer.Updater",
            InstalledVersion: null,
            AvailableVersion: "v1.0.0",
            ApplicationStatus.NotInstalled);

        Assert.False(response.UpdateAvailable);
    }
}
