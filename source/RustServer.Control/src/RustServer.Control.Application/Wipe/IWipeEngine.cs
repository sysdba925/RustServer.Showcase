using RustServer.Control.Domain.Wipe;

namespace RustServer.Control.Application.Wipe;

/// <summary>
/// Validates and executes known semantic wipe targets.
/// </summary>
public interface IWipeEngine
{
    string? ValidatePlan(WipePlan plan);

    Task<WipeExecutionReport> ExecuteAsync(
        WipePlan plan,
        CancellationToken cancellationToken = default);
}
