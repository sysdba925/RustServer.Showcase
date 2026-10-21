using System.Globalization;

namespace RustServer.Versions.Api.Versions;

internal static class SteamBuildId
{
    public static ulong Parse(string? value, string source)
    {
        if (!ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var buildId) || buildId == 0)
        {
            throw new InvalidOperationException($"The BuildID from {source} is missing, invalid, or zero.");
        }

        return buildId;
    }
}
