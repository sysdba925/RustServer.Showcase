namespace RustServer.Plugins.Features.PluginManagement.LocalGit;

public sealed record LocalGitStagingInspection(
    IReadOnlyList<string> StagedPaths,
    IReadOnlyList<string> UnstagedPaths,
    IReadOnlyList<string> UntrackedPaths)
{
    public bool IsCleanOutsideIndex => UnstagedPaths.Count == 0 && UntrackedPaths.Count == 0;
}
