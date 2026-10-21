namespace RustServer.Plugins.Features.PluginManagement.RemoteGit;

public enum RemoteGitPushStatus
{
    Succeeded,
    WorkspaceMissing,
    NotRepository,
    RemoteMissing,
    Failed
}
