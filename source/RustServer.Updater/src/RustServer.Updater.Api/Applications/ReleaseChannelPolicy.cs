using System.Text.RegularExpressions;

namespace RustServer.Updater.Api.Applications;

/// <summary>Validates the explicit release-tag shape permitted by each Updater channel.</summary>
public static partial class ReleaseChannelPolicy
{
    /// <summary>Returns whether a tag belongs to the supplied installation-wide channel.</summary>
    public static bool IsAllowedTag(string? tag, ReleaseChannel channel) => channel switch
    {
        ReleaseChannel.Development => tag is not null && DevelopmentTagPattern().IsMatch(tag),
        ReleaseChannel.Production => tag is not null && ProductionTagPattern().IsMatch(tag),
        _ => false
    };

    /// <summary>Rejects release tags that cannot be installed under the selected channel.</summary>
    public static void EnsureAllowedTag(string? tag, ReleaseChannel channel)
    {
        if (!IsAllowedTag(tag, channel))
        {
            var requiredShape = channel == ReleaseChannel.Development
                ? "vX.Y.Z-dev.N"
                : "vX.Y.Z";
            throw new PackagePreparationException($"Release tag is not permitted by the {channel} channel; expected {requiredShape}.");
        }
    }

    [GeneratedRegex("^v(?:0|[1-9][0-9]*)\\.(?:0|[1-9][0-9]*)\\.(?:0|[1-9][0-9]*)-dev\\.(?:0|[1-9][0-9]*)$", RegexOptions.CultureInvariant)]
    private static partial Regex DevelopmentTagPattern();

    [GeneratedRegex("^v(?:0|[1-9][0-9]*)\\.(?:0|[1-9][0-9]*)\\.(?:0|[1-9][0-9]*)$", RegexOptions.CultureInvariant)]
    private static partial Regex ProductionTagPattern();
}
