using RustServer.Plugins.Features.PluginManagement.LocalGit;
using RustServer.Plugins.Features.PluginManagement.Models.Git;
using RustServer.Plugins.Features.PluginManagement.RemoteGit;

namespace RustServer.Plugins.Features.PluginManagement.Git;

public sealed record GitRepositoryStatusSnapshot(
    RepositoryId Repository,
    bool IsAvailable,
    LocalGitRepositoryState? Local,
    RemoteGitRepositoryState? Remote);
