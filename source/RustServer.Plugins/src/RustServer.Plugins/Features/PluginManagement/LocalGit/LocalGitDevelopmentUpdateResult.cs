namespace RustServer.Plugins.Features.PluginManagement.LocalGit;

public sealed record LocalGitDevelopmentUpdateResult(
    bool WasFastForwarded,
    LocalGitRepositoryState State);
