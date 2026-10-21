using RustServer.Plugins.Features.PluginManagement.Models.Git;

namespace RustServer.Plugins.Features.PluginManagement.LocalGit;

public sealed record LocalGitRepositoryState(
    RepositoryId Repository,
    string WorkspacePath,
    bool IsRepository,
    string? Branch,
    GitCommitSha? Head,
    bool? IsClean,
    string? TrackingBranch,
    int? Ahead,
    int? Behind,
    LocalGitRelation Relation,
    LocalGitDiagnostic Diagnostic);
