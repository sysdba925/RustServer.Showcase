namespace RustServer.Plugins.Features.PluginManagement.LocalGit;

public enum LocalGitDiagnostic
{
    None,
    WorkspaceMissing,
    NotRepository,
    DetachedHead,
    HeadUnavailable,
    UnsupportedBranch
}
