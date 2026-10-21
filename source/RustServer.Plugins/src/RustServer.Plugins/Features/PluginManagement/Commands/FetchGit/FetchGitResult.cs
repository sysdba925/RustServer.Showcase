using RustServer.Plugins.Features.PluginManagement.Models.Operations;
using RustServer.Plugins.Features.PluginManagement.RemoteGit;

namespace RustServer.Plugins.Features.PluginManagement.Commands.FetchGit;

public sealed record FetchGitResult(
    OperationId? OperationId,
    IReadOnlyList<RemoteGitFetchResult> RepositoryResults,
    FetchGitFailure? Failure,
    FetchGitRejection? Rejection = null)
{
    public bool Succeeded => Failure is null && Rejection is null;
}
