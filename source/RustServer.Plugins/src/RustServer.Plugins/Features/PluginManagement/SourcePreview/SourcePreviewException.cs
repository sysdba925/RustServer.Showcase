using RustServer.Plugins.Features.PluginManagement.Models.Git;
using RustServer.Plugins.Features.PluginManagement.Runtime;

namespace RustServer.Plugins.Features.PluginManagement.SourcePreview;

public sealed class SourcePreviewException : Exception
{
    public SourcePreviewException(
        SourcePreviewFailure failure,
        string message,
        IReadOnlyList<RepositoryId>? missingRepositories = null,
        RepositoryId? repository = null,
        string? filePath = null,
        RuntimeAvailabilityStatus? runtimeStatus = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Failure = failure;
        MissingRepositories = missingRepositories?.ToArray() ?? [];
        Repository = repository;
        FilePath = filePath;
        RuntimeStatus = runtimeStatus;
    }

    public SourcePreviewFailure Failure { get; }

    public IReadOnlyList<RepositoryId> MissingRepositories { get; }

    public RepositoryId? Repository { get; }

    public string? FilePath { get; }

    public RuntimeAvailabilityStatus? RuntimeStatus { get; }
}
