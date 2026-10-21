using RustServer.Plugins.Features.PluginManagement.Models.Operations;

namespace RustServer.Plugins.Features.PluginManagement.Commands.ApplySource;

public sealed record ApplySourceResult(
    OperationId? OperationId,
    ApplySourceFailure? Failure,
    ApplySourceRejection? Rejection = null)
{
    public bool Succeeded => Failure is null && Rejection is null;
}
