using RustServer.Plugins.Features.PluginManagement.Models.Git;

namespace RustServer.Plugins.Features.PluginManagement.Commands.GitSync;

public sealed record GitSyncCommand(RepositoryId Repository);
