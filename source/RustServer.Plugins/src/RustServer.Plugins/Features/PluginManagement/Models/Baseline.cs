using RustServer.Plugins.Features.PluginManagement.Models.Git;

namespace RustServer.Plugins.Features.PluginManagement.Models;

public sealed record Baseline
{
    public Baseline(
        PluginEnvironment environment,
        Source source,
        GitCommitSha pluginsCommit,
        GitCommitSha configsCommit,
        DateTimeOffset appliedAt)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(pluginsCommit);
        ArgumentNullException.ThrowIfNull(configsCommit);

        if (!EnvironmentSourcePolicy.IsAllowed(environment, source))
        {
            throw new ArgumentException(
                $"Source '{source.Kind}' is not allowed in environment '{environment}'.",
                nameof(source));
        }

        Environment = environment;
        Source = source;
        PluginsCommit = pluginsCommit;
        ConfigsCommit = configsCommit;
        AppliedAt = appliedAt;
    }

    public PluginEnvironment Environment { get; }

    public Source Source { get; }

    public Release? Release => Source.Release;

    public GitCommitSha PluginsCommit { get; }

    public GitCommitSha ConfigsCommit { get; }

    public DateTimeOffset AppliedAt { get; }
}
