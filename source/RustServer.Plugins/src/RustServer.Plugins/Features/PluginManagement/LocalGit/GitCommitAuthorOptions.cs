namespace RustServer.Plugins.Features.PluginManagement.LocalGit;

public sealed class GitCommitAuthorOptions
{
    public const string SectionName = "PluginManagement:GitCommitAuthor";

    public string Name { get; set; } = "Rust Server Platform";

    public string Email { get; set; } = "rustserver@local";
}
