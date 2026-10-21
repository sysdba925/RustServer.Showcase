using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RustServer.Updater.Api.Applications;

/// <summary>Metadata and authenticated asset endpoints for one exact GitHub Release.</summary>
public sealed record GitHubRelease(
    string TagName,
    bool Prerelease,
    bool Draft,
    IReadOnlyCollection<GitHubReleaseAsset> Assets);

/// <summary>A GitHub asset API endpoint. Browser download URLs are intentionally not represented.</summary>
public sealed record GitHubReleaseAsset(string Name, Uri ApiUrl);

/// <summary>Queries GitHub Releases and streams assets from their authenticated API endpoints.</summary>
public interface IGitHubReleaseClient
{
    /// <summary>Gets the non-draft release for one exact tag and verifies its channel metadata.</summary>
    Task<GitHubRelease> GetReleaseAsync(
        ApplicationRegistration application,
        string releaseTag,
        ReleaseChannel channel,
        CancellationToken cancellationToken);

    /// <summary>Streams an asset from the GitHub asset API into a newly created local file.</summary>
    Task DownloadAssetAsync(Uri assetApiUrl, string destinationPath, long maximumBytes, CancellationToken cancellationToken);
}

/// <summary>
/// GitHub Releases API client. Authentication is supplied solely by the established systemd credential provider.
/// </summary>
public sealed class GitHubReleaseClient : IGitHubReleaseClient
{
    private const int MaximumRedirects = 5;
    private readonly HttpClient _httpClient;
    private readonly IGitHubTokenProvider _tokenProvider;

    /// <summary>Creates the production client using the named GitHub Releases HTTP client.</summary>
    public GitHubReleaseClient(IHttpClientFactory httpClientFactory, IGitHubTokenProvider tokenProvider)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        _httpClient = httpClientFactory.CreateClient("GitHubReleases");
        _tokenProvider = tokenProvider;
    }

    /// <summary>Creates a client for tests with a fake HTTP handler.</summary>
    public GitHubReleaseClient(HttpClient httpClient, IGitHubTokenProvider tokenProvider)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _tokenProvider = tokenProvider ?? throw new ArgumentNullException(nameof(tokenProvider));
    }

    /// <inheritdoc />
    public async Task<GitHubRelease> GetReleaseAsync(
        ApplicationRegistration application,
        string releaseTag,
        ReleaseChannel channel,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(application);
        ReleaseChannelPolicy.EnsureAllowedTag(releaseTag, channel);
        var (owner, repository) = ParseRepository(application.ReleaseRepository);
        var token = _tokenProvider.GetToken();
        var apiPath = $"repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repository)}/releases/tags/{Uri.EscapeDataString(releaseTag)}";

        using var response = await SendAsync(new Uri(_httpClient.BaseAddress!, apiPath), token, "application/vnd.github+json", cancellationToken);
        EnsureSuccess(response.StatusCode);

        GitHubReleaseDocument? document;
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            document = await JsonSerializer.DeserializeAsync<GitHubReleaseDocument>(stream, cancellationToken: cancellationToken);
        }
        catch (JsonException)
        {
            throw new PackagePreparationException("GitHub release metadata is invalid.");
        }

        if (document is null
            || !string.Equals(document.TagName, releaseTag, StringComparison.Ordinal)
            || document.Draft
            || !ReleaseChannelPolicy.IsAllowedTag(document.TagName, channel)
            || (channel == ReleaseChannel.Development && !document.Prerelease)
            || (channel == ReleaseChannel.Production && document.Prerelease))
        {
            throw new PackagePreparationException("The requested GitHub Release is unavailable for the configured release channel.");
        }

        var assetPrefix = $"/repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repository)}/releases/assets/";
        var assets = (document.Assets ?? [])
            .Where(asset => !string.IsNullOrWhiteSpace(asset.Name) && IsAssetApiUrl(asset.Url, assetPrefix))
            .Select(asset => new GitHubReleaseAsset(asset.Name!, new Uri(asset.Url!, UriKind.Absolute)))
            .ToArray();
        return new GitHubRelease(document.TagName!, document.Prerelease, document.Draft, assets);
    }

    /// <inheritdoc />
    public async Task DownloadAssetAsync(Uri assetApiUrl, string destinationPath, long maximumBytes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(assetApiUrl);
        if (maximumBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        }

        if (!IsGitHubApiOrigin(assetApiUrl))
        {
            throw new PackagePreparationException("Release asset does not use the configured GitHub API.");
        }

        var token = _tokenProvider.GetToken();
        using var response = await SendAsync(assetApiUrl, token, "application/octet-stream", cancellationToken);
        EnsureSuccess(response.StatusCode);
        if (response.Content.Headers.ContentLength is long length && length > maximumBytes)
        {
            throw new PackagePreparationException("Downloaded release asset exceeds the configured size limit.");
        }

        var directory = Path.GetDirectoryName(destinationPath)
            ?? throw new ArgumentException("Asset destination must have a parent directory.", nameof(destinationPath));
        Directory.CreateDirectory(directory);
        try
        {
            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var destination = new FileStream(
                destinationPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 81_920,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var buffer = new byte[81_920];
            long totalBytes = 0;
            while (true)
            {
                var read = await source.ReadAsync(buffer, cancellationToken);
                if (read == 0)
                {
                    break;
                }

                totalBytes = checked(totalBytes + read);
                if (totalBytes > maximumBytes)
                {
                    throw new PackagePreparationException("Downloaded release asset exceeds the configured size limit.");
                }

                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }

            await destination.FlushAsync(cancellationToken);
            destination.Flush(flushToDisk: true);
        }
        catch
        {
            TryDelete(destinationPath);
            throw;
        }
    }

    private async Task<HttpResponseMessage> SendAsync(
        Uri initialUri,
        string token,
        string accept,
        CancellationToken cancellationToken)
    {
        var currentUri = initialUri;
        for (var redirectCount = 0; ; redirectCount++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, currentUri);
            request.Headers.Accept.ParseAdd(accept);
            request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
            if (IsGitHubApiOrigin(currentUri))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }

            var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!IsRedirect(response.StatusCode))
            {
                return response;
            }

            if (redirectCount >= MaximumRedirects || response.Headers.Location is null)
            {
                response.Dispose();
                throw new PackagePreparationException("GitHub asset redirect could not be completed safely.");
            }

            var nextUri = new Uri(currentUri, response.Headers.Location);
            response.Dispose();
            if (nextUri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(nextUri.UserInfo))
            {
                throw new PackagePreparationException("GitHub asset redirect is not safe.");
            }

            currentUri = nextUri;
        }
    }

    private bool IsAssetApiUrl(string? url, string assetPrefix)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || !IsGitHubApiOrigin(uri))
        {
            return false;
        }

        return uri.AbsolutePath.StartsWith(assetPrefix, StringComparison.OrdinalIgnoreCase);
    }

    private bool IsGitHubApiOrigin(Uri uri)
    {
        var baseAddress = _httpClient.BaseAddress
            ?? throw new InvalidOperationException("GitHub HTTP client must have a base address.");
        return string.Equals(uri.Scheme, baseAddress.Scheme, StringComparison.OrdinalIgnoreCase)
            && string.Equals(uri.Host, baseAddress.Host, StringComparison.OrdinalIgnoreCase)
            && uri.Port == baseAddress.Port;
    }

    private static (string Owner, string Repository) ParseRepository(string releaseRepository)
    {
        var parts = releaseRepository.Split('/', StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || parts.Any(string.IsNullOrWhiteSpace))
        {
            throw new PackagePreparationException("The release repository must have the form 'owner/repository'.");
        }

        return (parts[0], parts[1]);
    }

    private static bool IsRedirect(HttpStatusCode statusCode) => statusCode is HttpStatusCode.Moved
        or HttpStatusCode.Redirect
        or HttpStatusCode.RedirectMethod
        or HttpStatusCode.TemporaryRedirect
        or HttpStatusCode.PermanentRedirect;

    private static void EnsureSuccess(HttpStatusCode statusCode)
    {
        if ((int)statusCode is < 200 or >= 300)
        {
            throw new PackagePreparationException($"GitHub Releases request failed with HTTP {(int)statusCode}.");
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // The original download failure is more useful and remains non-secret.
        }
    }

    private sealed record GitHubReleaseDocument(
        [property: JsonPropertyName("tag_name")] string? TagName,
        [property: JsonPropertyName("prerelease")] bool Prerelease,
        [property: JsonPropertyName("draft")] bool Draft,
        [property: JsonPropertyName("assets")] IReadOnlyCollection<GitHubReleaseAssetDocument>? Assets);

    private sealed record GitHubReleaseAssetDocument(
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("url")] string? Url);
}
