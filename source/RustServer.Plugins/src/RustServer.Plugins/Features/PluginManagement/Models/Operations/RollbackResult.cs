namespace RustServer.Plugins.Features.PluginManagement.Models.Operations;

public enum RollbackStatus
{
    NotAttempted,
    Succeeded,
    Failed
}

public sealed record RollbackResult
{
    public RollbackResult(RollbackStatus status, string? message = null)
    {
        Status = status;
        Message = message;
    }

    public RollbackStatus Status { get; }

    public string? Message { get; }
}
