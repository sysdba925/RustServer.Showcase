using RustServer.Plugins.Features.PluginManagement.Models;
using RustServer.Plugins.Features.PluginManagement.Models.Git;

namespace RustServer.Plugins.Features.PluginManagement.ReleaseDiscovery;

public sealed record ReleaseDiscoveryEntry(
    Release Release,
    ReleaseAvailability Availability,
    GitCommitSha? PluginsHead,
    GitCommitSha? ConfigsHead);
