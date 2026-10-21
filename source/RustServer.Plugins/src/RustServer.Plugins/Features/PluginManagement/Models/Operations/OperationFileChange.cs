using RustServer.Plugins.Features.PluginManagement.Models.Git;

namespace RustServer.Plugins.Features.PluginManagement.Models.Operations;

public sealed record OperationFileChange
{
    public OperationFileChange(
        RepositoryId repository,
        string path,
        OperationChangeKind change)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Changed file path must contain text.", nameof(path));
        }

        Repository = repository;
        Path = path;
        Change = change;
    }

    public RepositoryId Repository { get; }

    public string Path { get; }

    public OperationChangeKind Change { get; }
}
