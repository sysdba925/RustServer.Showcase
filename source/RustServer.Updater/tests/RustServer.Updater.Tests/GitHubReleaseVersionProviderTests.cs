using System.Net;
using System.Net.Http;
using RustServer.Updater.Api.Applications;

namespace RustServer.Updater.Tests;

public sealed class GitHubReleaseVersionProviderTests
{
    [Fact]
    public async Task GetAvailableVersionAsync_ReturnsNewestDevelopmentTagBySemanticVersion()
    {
        using var client = CreateClient(HttpStatusCode.OK, """
            [
              { "tag_name": "v1.0.0-dev.2", "prerelease": true, "draft": false },
              { "tag_name": "v1.0.0-dev.10", "prerelease": true, "draft": false },
              { "tag_name": "v1.0.0", "prerelease": false, "draft": false },
              { "tag_name": "v1.1.0-preview.1", "prerelease": true, "draft": false }
            ]
            """);
        var provider = CreateProvider(client, ReleaseChannel.Development);

        var version = await provider.GetAvailableVersionAsync(CreateApplication(), CancellationToken.None);

        Assert.Equal("v1.0.0-dev.10", version);
    }

    [Fact]
    public async Task GetAvailableVersionAsync_ReturnsNewestStableTagForProduction()
    {
        using var client = CreateClient(HttpStatusCode.OK, """
            [
              { "tag_name": "v1.9.0", "prerelease": false, "draft": false },
              { "tag_name": "v1.10.0", "prerelease": false, "draft": false },
              { "tag_name": "v2.0.0-dev.1", "prerelease": true, "draft": false }
            ]
            """);
        var provider = CreateProvider(client, ReleaseChannel.Production);

        var version = await provider.GetAvailableVersionAsync(CreateApplication(), CancellationToken.None);

        Assert.Equal("v1.10.0", version);
    }

    [Fact]
    public async Task GetAvailableVersionsAsync_ReturnsOnlyConfiguredChannelNewestFirst()
    {
        using var client = CreateClient(HttpStatusCode.OK, """
            [
              { "tag_name": "v1.0.0-dev.2", "prerelease": true, "draft": false },
              { "tag_name": "v1.0.0-dev.10", "prerelease": true, "draft": false },
              { "tag_name": "v1.1.0-dev.1", "prerelease": true, "draft": false },
              { "tag_name": "v1.1.0", "prerelease": false, "draft": false },
              { "tag_name": "v9.0.0-dev.1", "prerelease": false, "draft": false },
              { "tag_name": "v2.0.0-dev.1", "prerelease": true, "draft": true }
            ]
            """);
        var provider = CreateProvider(client, ReleaseChannel.Development);

        var versions = await provider.GetAvailableVersionsAsync(CreateApplication(), CancellationToken.None);

        Assert.Equal(["v1.1.0-dev.1", "v1.0.0-dev.10", "v1.0.0-dev.2"], versions);
    }

    [Fact]
    public async Task GetAvailableVersionAsync_UsesTheInstallationWideReleaseChannel()
    {
        using var client = CreateClient(HttpStatusCode.OK, """
            [
              { "tag_name": "v1.10.0", "prerelease": false, "draft": false },
              { "tag_name": "v2.0.0-dev.1", "prerelease": true, "draft": false }
            ]
            """);
        var provider = new GitHubReleaseVersionProvider(
            client,
            new FixedReleaseChannelProvider(ReleaseChannel.Production),
            new FixedTokenProvider());

        var version = await provider.GetAvailableVersionAsync(CreateApplication(), CancellationToken.None);

        Assert.Equal("v1.10.0", version);
    }

    [Fact]
    public async Task GetAvailableVersionAsync_ReturnsNullForAnEmptyReleaseList()
    {
        using var client = CreateClient(HttpStatusCode.OK, "[]");
        var provider = CreateProvider(client, ReleaseChannel.Development);

        var version = await provider.GetAvailableVersionAsync(CreateApplication(), CancellationToken.None);

        Assert.Null(version);
    }

    [Fact]
    public async Task GetAvailableVersionAsync_ThrowsForGitHubFailure()
    {
        using var client = CreateClient(HttpStatusCode.InternalServerError, "{}");
        var provider = CreateProvider(client, ReleaseChannel.Development);

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            provider.GetAvailableVersionAsync(CreateApplication(), CancellationToken.None));
    }

    private static HttpClient CreateClient(HttpStatusCode statusCode, string content) => new(
        new StubHttpMessageHandler(_ => new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(content)
        }))
    {
        BaseAddress = new Uri("https://api.github.test/")
    };

    private static GitHubReleaseVersionProvider CreateProvider(HttpClient client, ReleaseChannel channel) => new(
        client,
        new FixedReleaseChannelProvider(channel),
        new FixedTokenProvider());

    private static ApplicationRegistration CreateApplication() => new(
        "RustServer.Updater",
        "owner/repository",
        "/opt/rustserver/updater",
        "rustserver-updater.service",
        "RustServer.Updater.Api.dll");

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responseFactory(request));
    }

    private sealed class FixedReleaseChannelProvider(ReleaseChannel channel) : IReleaseChannelProvider
    {
        public ReleaseChannel GetChannel() => channel;
    }

    private sealed class FixedTokenProvider : IGitHubTokenProvider
    {
        public string GetToken() => "test-token";
    }
}
