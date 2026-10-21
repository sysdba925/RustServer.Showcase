using RustServer.Plugins.Features.PluginManagement.Models.Git;

namespace RustServer.Plugins.Features.PluginManagement.LocalGit;

public sealed class LocalGitOptions
{
    public const string SectionName = "PluginManagement:Repositories";

    public WorkspaceOptions Plugins { get; set; } = new();

    public WorkspaceOptions Configs { get; set; } = new();

    public string GetWorkspacePath(RepositoryId repository) =>
        repository switch
        {
            RepositoryId.Plugins => Plugins.WorkspacePath,
            RepositoryId.Configs => Configs.WorkspacePath,
            _ => throw new ArgumentOutOfRangeException(nameof(repository), repository, "Unsupported repository.")
        };
}

public sealed class WorkspaceOptions
{
    public string WorkspacePath { get; set; } = string.Empty;
}
