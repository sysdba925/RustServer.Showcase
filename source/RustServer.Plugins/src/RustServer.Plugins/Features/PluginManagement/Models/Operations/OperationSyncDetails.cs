using RustServer.Plugins.Features.PluginManagement.LocalGit;
using RustServer.Plugins.Features.PluginManagement.Models.Git;

namespace RustServer.Plugins.Features.PluginManagement.Models.Operations;

public enum OperationSyncBehavior
{
    None,
    FastForward,
    Push,
    MergeAndPush
}

public sealed record OperationSyncDetails(
    RepositoryId Repository,
    LocalGitRelation InitialRelation,
    OperationSyncBehavior Behavior,
    bool ManualInterventionRequired);
