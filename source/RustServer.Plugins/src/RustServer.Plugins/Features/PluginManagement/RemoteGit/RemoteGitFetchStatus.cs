namespace RustServer.Plugins.Features.PluginManagement.RemoteGit;

public enum RemoteGitFetchStatus
{
    Succeeded,
    WorkspaceMissing,
    NotRepository,
    RemoteMissing,
    Failed
}
