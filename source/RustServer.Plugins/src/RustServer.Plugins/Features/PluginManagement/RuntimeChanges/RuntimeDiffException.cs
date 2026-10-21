using RustServer.Plugins.Features.PluginManagement.Models.Git;
using RustServer.Plugins.Features.PluginManagement.Runtime;

namespace RustServer.Plugins.Features.PluginManagement.RuntimeChanges;

public enum RuntimeDiffFailure
{
    BaselineGitObjectUnavailable,
    RuntimeUnavailable
}

public sealed class RuntimeDiffException(
    RuntimeDiffFailure failure,
    RepositoryId? repository = null,
    RuntimeAvailabilityStatus? runtimeStatus = null,
    Exception? innerException = null)
    : Exception("Runtime Diff could not read a required dependency.", innerException)
{
    public RuntimeDiffFailure Failure { get; } = failure;

    public RepositoryId? Repository { get; } = repository;

    public RuntimeAvailabilityStatus? RuntimeStatus { get; } = runtimeStatus;
}
