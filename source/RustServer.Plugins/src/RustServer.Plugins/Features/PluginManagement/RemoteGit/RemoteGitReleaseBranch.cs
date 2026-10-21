using RustServer.Plugins.Features.PluginManagement.Models.Git;

namespace RustServer.Plugins.Features.PluginManagement.RemoteGit;

public sealed record RemoteGitReleaseBranch(string Branch, GitCommitSha Head);
