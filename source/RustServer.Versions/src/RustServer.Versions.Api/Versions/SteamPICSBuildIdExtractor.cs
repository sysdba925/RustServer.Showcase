using SteamKit2;

namespace RustServer.Versions.Api.Versions;

internal static class SteamPICSBuildIdExtractor
{
    public static ulong Extract(KeyValue appMetadata, string branch)
    {
        ArgumentNullException.ThrowIfNull(appMetadata);

        if (string.IsNullOrWhiteSpace(branch))
        {
            throw new ArgumentException("A Steam branch must be provided.", nameof(branch));
        }

        var depots = GetRequiredChild(appMetadata, "depots", "Steam PICS app metadata");
        var branches = GetRequiredChild(depots, "branches", "Steam PICS depots metadata");
        var branchMetadata = GetRequiredChild(branches, branch, "Steam PICS branches metadata");
        var buildId = GetRequiredChild(branchMetadata, "buildid", $"Steam PICS branch '{branch}'");

        return SteamBuildId.Parse(buildId.AsString(), $"Steam PICS branch '{branch}'");
    }

    private static KeyValue GetRequiredChild(KeyValue parent, string name, string parentDescription)
    {
        var child = parent[name];

        if (ReferenceEquals(child, KeyValue.Invalid))
        {
            throw new InvalidOperationException($"{parentDescription} does not contain '{name}'.");
        }

        return child;
    }
}
