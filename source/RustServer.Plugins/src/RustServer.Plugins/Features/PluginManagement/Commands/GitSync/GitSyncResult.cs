using RustServer.Plugins.Features.PluginManagement.Models.Operations;

namespace RustServer.Plugins.Features.PluginManagement.Commands.GitSync;

public sealed record GitSyncResult(
    OperationId? OperationId,
    GitSyncFailure? Failure,
    GitSyncRejection? Rejection = null)
{
    public bool Succeeded => Failure is null && Rejection is null;
}
