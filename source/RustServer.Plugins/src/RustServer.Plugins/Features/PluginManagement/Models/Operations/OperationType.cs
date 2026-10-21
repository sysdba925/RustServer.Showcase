namespace RustServer.Plugins.Features.PluginManagement.Models.Operations;

public enum OperationType
{
    FetchGit,
    GitSync,
    ApplySource,
    CommitRuntime,
    RevertRuntime
}
