using RustServer.Plugins.Features.PluginManagement.Models.Git;

namespace RustServer.Plugins.Features.PluginManagement.Runtime;

public sealed record ManagedFile
{
    internal ManagedFile(
        RepositoryId repository,
        string repositoryRelativePath,
        string runtimeRelativePath)
    {
        Repository = repository;
        RepositoryRelativePath = repositoryRelativePath;
        RuntimeRelativePath = runtimeRelativePath;
    }

    public RepositoryId Repository { get; }

    public string RepositoryRelativePath { get; }

    public string RuntimeRelativePath { get; }
}
