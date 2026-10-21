using System.Text.Json;

namespace RustServer.Versions.Api.Versions;

/// <summary>
/// Reads the latest published Oxide.Rust release from GitHub Releases.
/// </summary>
public sealed class GitHubOxideAvailableVersionReader(HttpClient httpClient) : IOxideAvailableVersionReader
{
    private static readonly Uri LatestReleaseUri = new(
        "https://api.github.com/repos/OxideMod/Oxide.Rust/releases/latest");
    private const string LinuxAssetName = "Oxide.Rust-linux.zip";

    public async Task<OxideVersion> ReadVersionAsync(CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(
            LatestReleaseUri,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"GitHub latest Oxide.Rust release request failed with HTTP {(int) response.StatusCode} ({response.StatusCode}).",
                inner: null,
                response.StatusCode);
        }

        try
        {
            await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(responseStream, cancellationToken: cancellationToken);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException("GitHub latest Oxide.Rust release response must be a JSON object.");
            }

            if (!root.TryGetProperty("tag_name", out var tagNameElement) ||
                tagNameElement.ValueKind != JsonValueKind.String)
            {
                throw new InvalidOperationException(
                    "GitHub latest Oxide.Rust release response does not contain a string tag_name.");
            }

            if (!root.TryGetProperty("assets", out var assetsElement) ||
                assetsElement.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidOperationException(
                    "GitHub latest Oxide.Rust release response does not contain an assets array.");
            }

            var includesLinuxAsset = assetsElement.EnumerateArray().Any(asset =>
                asset.ValueKind == JsonValueKind.Object &&
                asset.TryGetProperty("name", out var nameElement) &&
                nameElement.ValueKind == JsonValueKind.String &&
                string.Equals(nameElement.GetString(), LinuxAssetName, StringComparison.Ordinal));

            if (!includesLinuxAsset)
            {
                throw new InvalidOperationException(
                    $"GitHub latest Oxide.Rust release does not contain the required {LinuxAssetName} asset.");
            }

            return OxideVersion.Parse(tagNameElement.GetString(), "the GitHub latest Oxide.Rust release tag_name");
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                "GitHub latest Oxide.Rust release response contains malformed JSON.",
                exception);
        }
    }
}
