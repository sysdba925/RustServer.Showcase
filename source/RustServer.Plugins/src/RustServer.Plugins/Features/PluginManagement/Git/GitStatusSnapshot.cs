namespace RustServer.Plugins.Features.PluginManagement.Git;

public sealed record GitStatusSnapshot(
    GitReadiness Status,
    GitRepositoryStatusSnapshot Plugins,
    GitRepositoryStatusSnapshot Configs,
    DateTimeOffset? LastFetchAt);
