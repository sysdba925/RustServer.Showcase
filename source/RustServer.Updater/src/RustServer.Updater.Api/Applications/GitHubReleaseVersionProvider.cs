using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace RustServer.Updater.Api.Applications;

/// <summary>
/// Discovers release versions from the GitHub Releases API.
/// </summary>
public sealed class GitHubReleaseVersionProvider : IAvailableVersionProvider
{
    private readonly HttpClient _httpClient;
    private readonly IReleaseChannelProvider _releaseChannelProvider;
    private readonly IGitHubTokenProvider _tokenProvider;

    /// <summary>Creates the production provider with installation-wide channel and systemd credential support.</summary>
    public GitHubReleaseVersionProvider(
        HttpClient httpClient,
        IReleaseChannelProvider releaseChannelProvider,
        IGitHubTokenProvider tokenProvider)
    {
        _httpClient = httpClient;
        _releaseChannelProvider = releaseChannelProvider;
        _tokenProvider = tokenProvider;
    }

    /// <inheritdoc />
    public async Task<string?> GetAvailableVersionAsync(ApplicationRegistration application, CancellationToken cancellationToken)
        => (await GetAvailableVersionsAsync(application, cancellationToken)).FirstOrDefault();

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetAvailableVersionsAsync(
        ApplicationRegistration application,
        CancellationToken cancellationToken)
    {
        var repositoryParts = application.ReleaseRepository.Split('/', StringSplitOptions.TrimEntries);
        if (repositoryParts.Length != 2 || repositoryParts.Any(string.IsNullOrWhiteSpace))
        {
            throw new InvalidOperationException("The release repository must have the form 'owner/repository'.");
        }

        var owner = Uri.EscapeDataString(repositoryParts[0]);
        var repository = Uri.EscapeDataString(repositoryParts[1]);
        var releases = new List<GitHubRelease>();
        for (var page = 1; ; page++)
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"repos/{owner}/{repository}/releases?per_page=100&page={page}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _tokenProvider.GetToken());

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var releasesPage = await response.Content.ReadFromJsonAsync<List<GitHubRelease>>(cancellationToken: cancellationToken)
                ?? [];
            releases.AddRange(releasesPage);

            if (releasesPage.Count < 100)
            {
                break;
            }
        }

        return releases
            .Where(release => !release.Draft && SemanticVersion.TryParse(release.TagName, out _))
            .Select(release => new VersionedRelease(release, GetVersion(release.TagName)))
            .Where(versionedRelease => IsInChannel(
                versionedRelease.Release,
                versionedRelease.Version,
                _releaseChannelProvider.GetChannel()))
            .OrderByDescending(versionedRelease => versionedRelease.Version, SemanticVersionComparer.Instance)
            .Select(versionedRelease => versionedRelease.Release.TagName)
            .ToArray();
    }

    private static SemanticVersion GetVersion(string tagName)
    {
        _ = SemanticVersion.TryParse(tagName, out var version);
        return version!;
    }

    private static bool IsInChannel(GitHubRelease release, SemanticVersion version, ReleaseChannel channel) => channel switch
    {
        ReleaseChannel.Development => release.Prerelease
                                      && ReleaseChannelPolicy.IsAllowedTag(release.TagName, channel),
        ReleaseChannel.Production => !release.Prerelease
                                     && ReleaseChannelPolicy.IsAllowedTag(release.TagName, channel),
        _ => false
    };

    private sealed record GitHubRelease(
        [property: JsonPropertyName("tag_name")] string TagName,
        [property: JsonPropertyName("prerelease")] bool Prerelease,
        [property: JsonPropertyName("draft")] bool Draft);

    private sealed record VersionedRelease(GitHubRelease Release, SemanticVersion Version);

    private sealed class SemanticVersionComparer : IComparer<SemanticVersion>
    {
        public static SemanticVersionComparer Instance { get; } = new();

        public int Compare(SemanticVersion? x, SemanticVersion? y) => x?.CompareTo(y) ?? (y is null ? 0 : -1);
    }
}
