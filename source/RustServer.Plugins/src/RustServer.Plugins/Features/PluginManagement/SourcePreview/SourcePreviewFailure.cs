namespace RustServer.Plugins.Features.PluginManagement.SourcePreview;

public enum SourcePreviewFailure
{
    SourceNotAllowed,
    ReleaseIncomplete,
    TargetSourceUnavailable,
    GitObjectUnavailable,
    RuntimeUnavailable
}
