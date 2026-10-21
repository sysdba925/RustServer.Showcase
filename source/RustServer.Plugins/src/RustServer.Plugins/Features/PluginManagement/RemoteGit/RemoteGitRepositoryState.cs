using RustServer.Plugins.Features.PluginManagement.Models.Git;

namespace RustServer.Plugins.Features.PluginManagement.RemoteGit;

public sealed record RemoteGitRepositoryState(
    RepositoryId Repository,
    bool IsRemoteConfigured,
    string? Branch,
    GitCommitSha? Head,
    RemoteGitDiagnostic Diagnostic);
