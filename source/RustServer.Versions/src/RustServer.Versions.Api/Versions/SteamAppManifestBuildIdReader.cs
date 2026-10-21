using SteamKit2;
using System.Globalization;

namespace RustServer.Versions.Api.Versions;

public sealed class SteamAppManifestBuildIdReader : IInstalledBuildIdReader
{
    private const uint RustDedicatedServerAppId = 258550;

    public ulong? ReadBuildId(string appManifestPath)
    {
        if (string.IsNullOrWhiteSpace(appManifestPath))
        {
            throw new InvalidOperationException("RustVersions:AppManifestPath must be configured.");
        }

        // A clean server has no Steam manifest until the first SteamCMD install.
        if (!File.Exists(appManifestPath))
        {
            return null;
        }

        var appState = KeyValue.LoadAsText(appManifestPath)
            ?? throw new InvalidOperationException("The Steam app manifest could not be read as VDF.");

        if (!string.Equals(appState.Name, "AppState", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The Steam app manifest does not contain an AppState object.");
        }

        var appId = appState["appid"].AsString();
        var buildId = appState["buildid"].AsString();

        if (!uint.TryParse(appId, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedAppId) ||
            parsedAppId != RustDedicatedServerAppId)
        {
            throw new InvalidOperationException(
                $"The Steam app manifest AppState:appid must be {RustDedicatedServerAppId}.");
        }

        return SteamBuildId.Parse(buildId, "the Steam app manifest");
    }
}
