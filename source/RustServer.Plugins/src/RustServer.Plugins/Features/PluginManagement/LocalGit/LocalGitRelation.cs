namespace RustServer.Plugins.Features.PluginManagement.LocalGit;

public enum LocalGitRelation
{
    Unknown,
    NoTracking,
    Equal,
    Ahead,
    Behind,
    Diverged
}
