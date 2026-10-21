namespace RustServer.Plugins.Features.PluginManagement.LocalGit;

public sealed record LocalGitCommit(
    string Message,
    string AuthorName,
    string AuthorEmail);
