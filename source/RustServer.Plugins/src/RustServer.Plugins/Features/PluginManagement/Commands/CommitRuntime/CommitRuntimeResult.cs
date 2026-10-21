using RustServer.Plugins.Features.PluginManagement.Models.Operations;

namespace RustServer.Plugins.Features.PluginManagement.Commands.CommitRuntime;

public sealed record CommitRuntimeResult(
    OperationId? OperationId,
    CommitRuntimeFailure? Failure,
    CommitRuntimeRejection? Rejection = null)
{
    public bool Succeeded => Failure is null && Rejection is null;
}
