namespace RustServer.Plugins.Features.PluginManagement.LocalGit;

public enum LocalGitMergeStatus
{
    Succeeded,
    Conflict,
    Failed
}

public sealed record LocalGitMergeResult(LocalGitMergeStatus Status);
