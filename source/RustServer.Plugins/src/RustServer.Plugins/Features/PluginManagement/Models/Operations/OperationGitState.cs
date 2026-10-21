using RustServer.Plugins.Features.PluginManagement.LocalGit;
using RustServer.Plugins.Features.PluginManagement.Models.Git;

namespace RustServer.Plugins.Features.PluginManagement.Models.Operations;

public sealed record OperationGitState(
    RepositoryId Repository,
    string? Branch,
    GitCommitSha? Head,
    LocalGitRelation Relation,
    string? Diagnostic);
