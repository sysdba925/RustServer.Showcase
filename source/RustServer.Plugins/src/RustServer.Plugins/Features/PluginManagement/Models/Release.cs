namespace RustServer.Plugins.Features.PluginManagement.Models;

public sealed record Release
{
    public const string BranchPrefix = "release/";

    public Release(string version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            throw new ArgumentException("Release version must contain text.", nameof(version));
        }

        if (version.StartsWith(BranchPrefix, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Release version must not include the '{BranchPrefix}' prefix.",
                nameof(version));
        }

        Version = version;
    }

    public string Version { get; }

    public string BranchName => $"{BranchPrefix}{Version}";
}
