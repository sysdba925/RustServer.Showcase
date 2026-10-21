using RustServer.Plugins.Features.PluginManagement.Models;
using RustServer.Plugins.Features.PluginManagement.Models.Git;

namespace RustServer.Plugins.Features.PluginManagement.LocalGit;

public sealed record LocalGitSourcePreparation(
    RepositoryId Repository,
    Source Source,
    GitCommitSha TargetHead,
    LocalGitSourcePreparationKind Kind);
