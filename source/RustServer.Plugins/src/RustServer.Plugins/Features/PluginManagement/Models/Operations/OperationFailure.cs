using RustServer.Plugins.Features.PluginManagement.Models.Git;

namespace RustServer.Plugins.Features.PluginManagement.Models.Operations;

public sealed record OperationFailure
{
    public OperationFailure(
        string phase,
        string code,
        string message,
        RepositoryId? repository = null,
        string? filePath = null)
    {
        if (string.IsNullOrWhiteSpace(phase))
        {
            throw new ArgumentException("Operation phase must contain text.", nameof(phase));
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException("Error code must contain text.", nameof(code));
        }

        if (string.IsNullOrWhiteSpace(message))
        {
            throw new ArgumentException("Error message must contain text.", nameof(message));
        }

        Phase = phase;
        Code = code;
        Message = message;
        Repository = repository;
        FilePath = filePath;
    }

    public string Phase { get; }

    public string Code { get; }

    public string Message { get; }

    public RepositoryId? Repository { get; }

    public string? FilePath { get; }
}
