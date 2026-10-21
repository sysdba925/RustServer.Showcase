using RustServer.Plugins.Features.PluginManagement.Models.Git;

namespace RustServer.Plugins.Features.PluginManagement.Runtime;

public sealed class ManagedFileMapping
{
    public const string ManagedRootDirectoryName = "oxide";

    public ManagedFile? FromRepositoryPath(RepositoryId repository, string repositoryRelativePath)
    {
        var segments = SplitLogicalPath(repositoryRelativePath);
        if (segments is null)
        {
            return null;
        }

        return repository switch
        {
            RepositoryId.Plugins => MapPluginRepositoryPath(segments),
            RepositoryId.Configs => MapConfigRepositoryPath(segments),
            _ => throw new ArgumentOutOfRangeException(nameof(repository), repository, "Unsupported repository.")
        };
    }

    public ManagedFile? FromRuntimePath(string runtimeRelativePath)
    {
        var segments = SplitLogicalPath(runtimeRelativePath);
        if (segments is null || !string.Equals(segments[0], ManagedRootDirectoryName, StringComparison.Ordinal))
        {
            return null;
        }

        if (segments.Length == 3 &&
            string.Equals(segments[1], "plugins", StringComparison.Ordinal) &&
            IsPluginScript(segments[2]))
        {
            return new ManagedFile(
                RepositoryId.Plugins,
                segments[2],
                string.Join('/', segments));
        }

        if (segments.Length >= 3 && IsConfigArea(segments[1]))
        {
            var repositoryPath = string.Join('/', segments[1..]);
            return new ManagedFile(RepositoryId.Configs, repositoryPath, string.Join('/', segments));
        }

        if (segments.Length == 2 && IsConfigRootFile(segments[1]))
        {
            return new ManagedFile(RepositoryId.Configs, segments[1], string.Join('/', segments));
        }

        return null;
    }

    private static ManagedFile? MapPluginRepositoryPath(IReadOnlyList<string> segments) =>
        segments.Count == 1 && IsPluginScript(segments[0])
            ? new ManagedFile(RepositoryId.Plugins, segments[0], $"oxide/plugins/{segments[0]}")
            : null;

    private static ManagedFile? MapConfigRepositoryPath(IReadOnlyList<string> segments)
    {
        if (segments.Count >= 2 && IsConfigArea(segments[0]))
        {
            return new ManagedFile(
                RepositoryId.Configs,
                string.Join('/', segments),
                $"oxide/{string.Join('/', segments)}");
        }

        return segments.Count == 1 && IsConfigRootFile(segments[0])
            ? new ManagedFile(RepositoryId.Configs, segments[0], $"oxide/{segments[0]}")
            : null;
    }

    private static bool IsPluginScript(string fileName) =>
        fileName.EndsWith(".cs", StringComparison.OrdinalIgnoreCase);

    private static bool IsConfigArea(string segment) =>
        segment is "config" or "data" or "lang" or "discord";

    private static bool IsConfigRootFile(string segment) =>
        segment is "oxide.config.json" or "discord.config.json";

    private static string[]? SplitLogicalPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) ||
            Path.IsPathRooted(path) ||
            path.Contains('\\') ||
            path.Contains(':'))
        {
            return null;
        }

        var segments = path.Split('/', StringSplitOptions.None);
        return segments.Length == 0 || segments.Any(segment =>
            string.IsNullOrWhiteSpace(segment) ||
            segment is "." or "..")
            ? null
            : segments;
    }
}
