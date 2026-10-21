namespace RustServer.Plugins.Features.PluginManagement.RemoteGit;

public enum RemoteGitDiagnostic
{
    None,
    WorkspaceMissing,
    NotRepository,
    RemoteMissing,
    TrackingRemoteUnsupported,
    TrackingReferenceMissing
}
