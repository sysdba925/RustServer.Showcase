using RustServer.Versions.Api.Versions;
using System.Net;
using System.Text;
using System.Text.Json;

namespace RustServer.Versions.Tests;

public sealed class GitHubOxideAvailableVersionReaderTests
{
    [Fact]
    public async Task ReadVersionAsync_ReadsTagNameWhenLinuxAssetIsPresent()
    {
        using var client = CreateClient(HttpStatusCode.OK, """
            {
              "tag_name": "2.0.7726",
              "assets": [
                { "name": "Oxide.Rust-linux.zip" },
                { "name": "Oxide.Rust-windows.zip" }
              ]
            }
            """);
        var reader = new GitHubOxideAvailableVersionReader(client);

        var version = await reader.ReadVersionAsync(CancellationToken.None);

        Assert.Equal("2.0.7726", version.ToString());
    }

    [Fact]
    public async Task ReadVersionAsync_ThrowsForNonSuccessResponse()
    {
        using var client = CreateClient(HttpStatusCode.ServiceUnavailable, "service unavailable");
        var reader = new GitHubOxideAvailableVersionReader(client);

        var exception = await Assert.ThrowsAsync<HttpRequestException>(
            () => reader.ReadVersionAsync(CancellationToken.None));

        Assert.Contains("503", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadVersionAsync_ThrowsForMalformedJson()
    {
        using var client = CreateClient(HttpStatusCode.OK, "{");
        var reader = new GitHubOxideAvailableVersionReader(client);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => reader.ReadVersionAsync(CancellationToken.None));

        Assert.Contains("malformed JSON", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadVersionAsync_ThrowsWhenTagNameIsMissing()
    {
        using var client = CreateClient(HttpStatusCode.OK, """
            { "assets": [{ "name": "Oxide.Rust-linux.zip" }] }
            """);
        var reader = new GitHubOxideAvailableVersionReader(client);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => reader.ReadVersionAsync(CancellationToken.None));

        Assert.Contains("tag_name", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("2.0")]
    [InlineData("v2.0.7726")]
    [InlineData("2.0.invalid")]
    public async Task ReadVersionAsync_ThrowsWhenTagNameIsNotAnOxideVersion(string tagName)
    {
        using var client = CreateClient(
            HttpStatusCode.OK,
            JsonSerializer.Serialize(new
            {
                tag_name = tagName,
                assets = new[] { new { name = "Oxide.Rust-linux.zip" } },
            }));
        var reader = new GitHubOxideAvailableVersionReader(client);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => reader.ReadVersionAsync(CancellationToken.None));

        Assert.Contains("three-component", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadVersionAsync_ThrowsWhenLinuxAssetIsMissing()
    {
        using var client = CreateClient(HttpStatusCode.OK, """
            {
              "tag_name": "2.0.7726",
              "assets": [{ "name": "Oxide.Rust-windows.zip" }]
            }
            """);
        var reader = new GitHubOxideAvailableVersionReader(client);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => reader.ReadVersionAsync(CancellationToken.None));

        Assert.Contains("Oxide.Rust-linux.zip", exception.Message, StringComparison.Ordinal);
    }

    private static HttpClient CreateClient(HttpStatusCode statusCode, string content)
    {
        return new HttpClient(new StaticHttpMessageHandler(statusCode, content));
    }

    private sealed class StaticHttpMessageHandler(HttpStatusCode statusCode, string content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(content, Encoding.UTF8, "application/json"),
            });
        }
    }
}
