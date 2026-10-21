namespace RustServer.Plugins.Features.PluginManagement.LocalGit;

public enum LocalGitWorkingChangeKind
{
    Added,
    Modified,
    Deleted
}

public sealed record LocalGitWorkingChange(
    string RepositoryRelativePath,
    LocalGitWorkingChangeKind Kind);
