using RustServer.Plugins.Features.PluginManagement.Models.Operations;

namespace RustServer.Plugins.Features.PluginManagement.Commands.RevertRuntime;

public sealed record RevertRuntimeResult(
    OperationId? OperationId,
    RevertRuntimeFailure? Failure,
    RevertRuntimeRejection? Rejection = null)
{
    public bool Succeeded => Failure is null && Rejection is null;
}
